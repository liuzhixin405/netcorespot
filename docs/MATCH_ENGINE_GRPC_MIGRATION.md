# 撮合引擎 gRPC 迁移方案

## 架构对比

### 当前架构（单进程 + Channel）
```
┌─────────────────────────────────────────┐
│          CryptoSpot.API (单进程)         │
├─────────────────────────────────────────┤
│  Controller → Service → MatchEngine     │
│                   ↓                     │
│              Channel<T>                 │
│                   ↓                     │
│          后台任务处理撮合                 │
└─────────────────────────────────────────┘
```

### 目标架构（微服务 + gRPC）
```
┌──────────────────┐         ┌────────────────────┐
│  CryptoSpot.API  │  gRPC   │ MatchEngine Service│
│                  │ ───────▶│                    │
│  - REST API      │         │ - 订单撮合          │
│  - 业务逻辑       │         │ - 资产结算          │
│  - 用户认证       │         │ - 订单簿管理        │
└──────────────────┘         └────────────────────┘
```

## 迁移步骤

### 1. 定义 gRPC 协议 (Proto)

创建 `Protos/matchengine.proto`:

```protobuf
syntax = "proto3";

package cryptospot.matchengine;

service MatchEngine {
  rpc PlaceOrder (PlaceOrderRequest) returns (PlaceOrderResponse);
  rpc CancelOrder (CancelOrderRequest) returns (CancelOrderResponse);
  rpc GetOrderBook (OrderBookRequest) returns (OrderBookResponse);
}

message PlaceOrderRequest {
  int64 order_id = 1;
  int64 user_id = 2;
  string symbol = 3;
  int32 side = 4;      // 0=Buy, 1=Sell
  int32 type = 5;      // 0=Market, 1=Limit
  string price = 6;    // decimal as string
  string quantity = 7; // decimal as string
}

message PlaceOrderResponse {
  bool success = 1;
  string error_message = 2;
  OrderDto order = 3;
}

message OrderDto {
  int64 id = 1;
  int64 user_id = 2;
  string symbol = 3;
  int32 side = 4;
  int32 status = 5;
  string price = 6;
  string quantity = 7;
  string filled_quantity = 8;
  int64 created_at = 9;
}

message CancelOrderRequest {
  int64 order_id = 1;
  int64 user_id = 2;
}

message CancelOrderResponse {
  bool success = 1;
  string error_message = 2;
}

message OrderBookRequest {
  string symbol = 1;
  int32 depth = 2;
}

message OrderBookResponse {
  string symbol = 1;
  repeated OrderBookLevel bids = 2;
  repeated OrderBookLevel asks = 3;
}

message OrderBookLevel {
  string price = 1;
  string quantity = 2;
}
```

### 2. 创建 MatchEngine gRPC 服务

```csharp
// CryptoSpot.MatchEngine.Grpc/Services/MatchEngineGrpcService.cs
public class MatchEngineGrpcService : MatchEngine.MatchEngineBase
{
    private readonly ChannelMatchEngineService _matchEngine;
    private readonly ILogger<MatchEngineGrpcService> _logger;

    public MatchEngineGrpcService(
        ChannelMatchEngineService matchEngine,
        ILogger<MatchEngineGrpcService> logger)
    {
        _matchEngine = matchEngine;
        _logger = logger;
    }

    public override async Task<PlaceOrderResponse> PlaceOrder(
        PlaceOrderRequest request,
        ServerCallContext context)
    {
        try
        {
            // Proto → Domain
            var order = new Order
            {
                Id = request.OrderId,
                UserId = request.UserId,
                Symbol = request.Symbol,
                Side = (OrderSide)request.Side,
                Type = (OrderType)request.Type,
                Price = decimal.Parse(request.Price),
                Quantity = decimal.Parse(request.Quantity)
            };

            // 调用撮合引擎
            var result = await _matchEngine.PlaceOrderAsync(order, request.Symbol);

            // Domain → Proto
            return new PlaceOrderResponse
            {
                Success = true,
                Order = MapToProto(result)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlaceOrder failed");
            return new PlaceOrderResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public override async Task<CancelOrderResponse> CancelOrder(
        CancelOrderRequest request,
        ServerCallContext context)
    {
        // 实现取消订单逻辑
        return new CancelOrderResponse { Success = true };
    }

    private OrderDto MapToProto(Order order)
    {
        return new OrderDto
        {
            Id = order.Id,
            UserId = order.UserId ?? 0,
            Symbol = order.Symbol,
            Side = (int)order.Side,
            Status = (int)order.Status,
            Price = order.Price?.ToString() ?? "0",
            Quantity = order.Quantity.ToString(),
            FilledQuantity = order.FilledQuantity.ToString(),
            CreatedAt = order.CreatedAt
        };
    }
}
```

### 3. 配置 MatchEngine 服务

```csharp
// CryptoSpot.MatchEngine.Grpc/Program.cs
var builder = WebApplication.CreateBuilder(args);

// 添加 gRPC
builder.Services.AddGrpc();

// 注册撮合引擎
builder.Services.AddSingleton<ChannelMatchEngineService>();
builder.Services.AddSingleton<InMemoryAssetStore>();
// ... 其他服务

var app = builder.Build();

// 映射 gRPC 服务
app.MapGrpcService<MatchEngineGrpcService>();

app.Run();
```

### 4. API 端创建 gRPC 客户端

```csharp
// CryptoSpot.Infrastructure/Services/GrpcMatchEngineClient.cs
public class GrpcMatchEngineClient : IMatchEngineService
{
    private readonly MatchEngine.MatchEngineClient _client;
    private readonly ILogger<GrpcMatchEngineClient> _logger;

    public GrpcMatchEngineClient(
        MatchEngine.MatchEngineClient client,
        ILogger<GrpcMatchEngineClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(Order order, string symbol)
    {
        try
        {
            var request = new PlaceOrderRequest
            {
                OrderId = order.Id,
                UserId = order.UserId ?? 0,
                Symbol = symbol,
                Side = (int)order.Side,
                Type = (int)order.Type,
                Price = order.Price?.ToString() ?? "0",
                Quantity = order.Quantity.ToString()
            };

            var response = await _client.PlaceOrderAsync(request);

            if (!response.Success)
            {
                throw new InvalidOperationException(response.ErrorMessage);
            }

            return MapFromProto(response.Order);
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC call failed");
            throw new InvalidOperationException("撮合引擎通信失败", ex);
        }
    }

    private Order MapFromProto(OrderDto dto)
    {
        return new Order
        {
            Id = dto.Id,
            UserId = dto.UserId,
            Symbol = dto.Symbol,
            Side = (OrderSide)dto.Side,
            Status = (OrderStatus)dto.Status,
            Price = decimal.Parse(dto.Price),
            Quantity = decimal.Parse(dto.Quantity),
            FilledQuantity = decimal.Parse(dto.FilledQuantity),
            CreatedAt = dto.CreatedAt
        };
    }
}
```

### 5. 注册 gRPC 客户端

```csharp
// CryptoSpot.API/Program.cs
builder.Services.AddGrpcClient<MatchEngine.MatchEngineClient>(options =>
{
    options.Address = new Uri("https://matchengine:5001");
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    return new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        EnableMultipleHttp2Connections = true
    };
});

// 替换实现
builder.Services.AddScoped<IMatchEngineService, GrpcMatchEngineClient>();
```

## 配置和部署

### Docker Compose

```yaml
version: '3.8'

services:
  api:
    build: ./src/CryptoSpot.API
    ports:
      - "5000:80"
    environment:
      - MatchEngine__GrpcEndpoint=https://matchengine:5001
    depends_on:
      - matchengine
      - mysql

  matchengine:
    build: ./src/CryptoSpot.MatchEngine.Grpc
    ports:
      - "5001:5001"
    environment:
      - ASPNETCORE_URLS=https://+:5001
      - ConnectionStrings__DefaultConnection=...
    depends_on:
      - mysql

  mysql:
    image: mysql:8.0
    environment:
      MYSQL_ROOT_PASSWORD: password
```

## 性能优化

### 1. 连接池管理
```csharp
builder.Services.AddGrpcClient<MatchEngine.MatchEngineClient>()
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        return new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
    });
```

### 2. 超时控制
```csharp
var response = await _client.PlaceOrderAsync(
    request,
    deadline: DateTime.UtcNow.AddSeconds(5));
```

### 3. 重试策略
```csharp
builder.Services.AddGrpcClient<MatchEngine.MatchEngineClient>()
    .AddTransientHttpErrorPolicy(policy =>
        policy.WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt))));
```

## 渐进式迁移

### 阶段 1：保持现有架构
- ✅ 当前实现（Channel + 单进程）
- 适用于：开发、测试、中小规模部署

### 阶段 2：添加 gRPC 层（可选）
- 保留 Channel 实现
- 添加 gRPC 服务端
- 通过配置切换（方便测试对比）

```csharp
// 根据配置选择实现
if (configuration.GetValue<bool>("UseGrpcMatchEngine"))
{
    builder.Services.AddScoped<IMatchEngineService, GrpcMatchEngineClient>();
}
else
{
    builder.Services.AddSingleton<IMatchEngineService, ChannelMatchEngineService>();
}
```

### 阶段 3：完全分离
- 部署独立的 MatchEngine 服务
- API 只通过 gRPC 通信
- 适用于：大规模生产环境、需要独立扩展

## 优势和考虑

### gRPC 优势
- ⚡ 高性能（HTTP/2、二进制协议）
- 🔄 双向流支持
- 🛡️ 强类型安全
- 📦 自动生成客户端代码
- 🌐 跨语言支持

### 需要考虑的问题
- 🔧 增加运维复杂度
- 📊 需要监控服务间通信
- 🔐 需要处理网络故障和超时
- 💾 可能需要引入服务发现

## 总结

当前的 Channel 实现适合：
- ✅ 开发和测试阶段
- ✅ 中小规模部署
- ✅ 简单运维需求

未来考虑 gRPC 迁移，当：
- 📈 需要独立扩展撮合引擎
- 🔧 需要独立部署和更新
- 🌐 需要多个服务访问撮合引擎
- 🚀 流量达到需要水平扩展的规模
