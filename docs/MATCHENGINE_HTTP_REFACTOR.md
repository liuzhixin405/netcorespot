# 撮合引擎 HTTP 化重构指南

## 架构目标

将撮合引擎从进程内 Channel 通信改为独立的 HTTP 服务，实现以下目标：

1. **独立部署**：撮合引擎和 API 服务可以独立部署、独立扩展
2. **数据持久化**：启动时从数据库加载，关闭时保存回数据库
3. **HTTP 通信**：通过 RESTful API 进行服务间通信
4. **解耦合**：两个服务之间不共享内存，只通过 HTTP 接口交互

## 已完成的工作

### 1. 撮合引擎项目改造

**文件**: `CryptoSpot.MatchEngine.csproj`
- ✅ 从 `Microsoft.NET.Sdk.Worker` 改为 `Microsoft.NET.Sdk.Web`
- ✅ 添加 ASP.NET Core 和 Swagger 支持

**文件**: `Controllers/MatchEngineController.cs` ✅ 已创建
- `POST /api/match/orders` - 提交订单
- `DELETE /api/match/orders/{orderId}` - 取消订单  
- `GET /api/match/assets/{userId}` - 查询用户资产
- `GET /api/match/orderbook/{symbol}` - 查询订单簿深度
- `GET /api/match/health` - 健康检查

**文件**: `Services/MatchEngineDataService.cs` ✅ 已创建
- `StartAsync`: 启动时从数据库加载交易对和用户资产
- `StopAsync`: 关闭时将内存中的资产保存回数据库

**文件**: `appsettings.json` ✅ 已配置
- 数据库连接字符串
- 服务监听端口: `http://localhost:5001`

### 2. API 层改造

**文件**: `Infrastructure/Services/HttpMatchEngineClient.cs` ✅ 已创建
- HTTP 客户端封装撮合引擎调用
- 方法: `SubmitOrderAsync`, `CancelOrderAsync`, `GetUserAssetsAsync`, `HealthCheckAsync`

**文件**: `API/appsettings.json` ✅ 已配置
```json
"MatchEngine": {
  "BaseUrl": "http://localhost:5001"
}
```

**文件**: `API/Program.cs` ✅ 部分完成
- 移除 `ChannelMatchEngineService`
- 注册 `HttpMatchEngineClient`

## 需要完成的工作

### 1. 修复撮合引擎核心逻辑

**问题**: `InMemoryMatchEngineService.cs` 与领域实体不匹配

**解决方案**:
```csharp
// 创建简化的内部订单模型用于撮合
public class MatchOrder
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Symbol { get; set; }
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal FilledQuantity { get; set; }
    public long Timestamp { get; set; }
}

// 修改 IOrderBook 接口适配简化模型
public interface IMatchOrderBook
{
    void AddOrder(MatchOrder order);
    MatchOrder? GetBestMatch(MatchOrder takerOrder);
    bool CancelOrder(long orderId);
    List<MatchOrder> GetBids(int depth);
    List<MatchOrder> GetAsks(int depth);
}
```

### 2. 修改 TradingService 使用 HTTP 客户端

**文件**: `Infrastructure/Services/TradingService.cs`

```csharp
public class TradingService : ITradingService
{
    private readonly HttpMatchEngineClient _matchEngine;
    private readonly IOrderRepository _orderRepository;
    
    public async Task<ApiResponseDto<OrderDto?>> PlaceOrderAsync(PlaceOrderRequestDto request)
    {
        // 1. 验证订单参数
        // 2. 调用 HTTP 撮合引擎
        var result = await _matchEngine.SubmitOrderAsync(
            request.UserId,
            request.Symbol,
            request.Side,
            request.Type,
            request.Price,
            request.Quantity
        );
        
        // 3. 保存订单到数据库
        // 4. 保存成交记录到数据库
        // 5. 返回结果
    }
}
```

### 3. 移除不再需要的代码

**删除文件**:
- `API/Services/MatchEngineInitializationService.cs` (API 不再负责撮合引擎数据)
- `MatchEngine/ChannelMatchEngineService.cs` (不再使用 Channel)
- `Infrastructure/Services/MatchEngineAdapter.cs` (不再需要适配器)

**从 API/Program.cs 移除**:
```csharp
// 删除这些行
builder.Services.AddSingleton<InMemoryAssetStore>();
builder.Services.AddHostedService<MatchEngineInitializationService>();
builder.Services.AddSingleton<ChannelMatchEngineService>();
```

### 4. 配置多端口运行

**启动命令**:
```bash
# 终端 1: 启动撮合引擎 (端口 5001)
cd src/CryptoSpot.MatchEngine
dotnet run

# 终端 2: 启动 API 服务 (端口 5000)
cd src/CryptoSpot.API
dotnet run
```

**配置文件**:
```json
// MatchEngine/appsettings.json
{
  "Urls": "http://localhost:5001"
}

// API/appsettings.json
{
  "Urls": "http://localhost:5000",
  "MatchEngine": {
    "BaseUrl": "http://localhost:5001"
  }
}
```

### 5. 生产环境部署

**Docker Compose** 配置:
```yaml
version: '3.8'
services:
  mysql:
    image: mysql:8.0
    environment:
      MYSQL_ROOT_PASSWORD: 123456
      MYSQL_DATABASE: CryptoSpotDb
    ports:
      - "3306:3306"

  matchengine:
    build: ./src/CryptoSpot.MatchEngine
    ports:
      - "5001:5001"
    environment:
      ConnectionStrings__DefaultConnection: "Server=mysql;Database=CryptoSpotDb;..."
    depends_on:
      - mysql

  api:
    build: ./src/CryptoSpot.API
    ports:
      - "5000:5000"
    environment:
      ConnectionStrings__DefaultConnection: "Server=mysql;Database=CryptoSpotDb;..."
      MatchEngine__BaseUrl: "http://matchengine:5001"
    depends_on:
      - mysql
      - matchengine
```

## 架构优势

### Before (Channel 架构)
```
┌─────────────────────────────────────┐
│         CryptoSpot.API              │
│  ┌──────────┐      ┌─────────────┐ │
│  │Controller│─────>│MatchEngine  │ │
│  └──────────┘      │  (Channel)  │ │
│                    └─────────────┘ │
│  InMemoryAssetStore (Shared)       │
└─────────────────────────────────────┘
```

问题:
- API 和撮合引擎绑定在同一个进程
- 无法独立扩展
- 内存共享导致耦合

### After (HTTP 架构)
```
┌──────────────────┐    HTTP     ┌────────────────────┐
│  CryptoSpot.API  │────────────>│  MatchEngine       │
│  (Port 5000)     │   REST API  │  (Port 5001)       │
│                  │             │                    │
│  Controllers     │             │  HTTP Controllers  │
│  Business Logic  │             │  Matching Logic    │
│  ↓               │             │  InMemoryAssets    │
│  Database        │             │  ↓                 │
└──────────────────┘             │  Database          │
                                 └────────────────────┘
```

优势:
- ✅ 独立部署和扩展
- ✅ 服务解耦
- ✅ 可以独立升级
- ✅ 更好的监控和健康检查
- ✅ 可以部署多个撮合引擎实例（按交易对分片）

## 数据流

### 下单流程
```
1. 前端 → API POST /api/trading/orders
2. API → MatchEngine POST /api/match/orders (HTTP)
3. MatchEngine 撮合并返回结果
4. API 保存订单和成交记录到数据库
5. API 返回结果给前端
```

### 启动流程
```
MatchEngine 启动:
1. 从数据库加载所有活跃交易对
2. 从数据库加载所有用户资产到内存
3. 开始监听 HTTP 端口 5001

API 启动:
1. 连接数据库
2. 配置 HttpClient 指向 MatchEngine (localhost:5001)
3. 开始监听 HTTP 端口 5000
```

### 关闭流程
```
MatchEngine 关闭:
1. 停止接受新订单
2. 将所有内存中的资产余额写回数据库
3. 清空内存
4. 退出进程

API 关闭:
1. 正常关闭数据库连接
2. 退出进程
```

## 性能考虑

### HTTP 开销
- 内网 HTTP 调用延迟: ~1-5ms
- 对比 Channel: ~0.01-0.1ms
- 权衡: 延迟略微增加，但获得更好的架构

### 优化方案
1. **连接池**: HttpClient 使用连接池复用 TCP 连接
2. **批量操作**: 提供批量下单 API 减少往返次数
3. **gRPC 升级**: 如果性能成为瓶颈，可以升级为 gRPC (参考 MATCH_ENGINE_GRPC_MIGRATION.md)

## 下一步行动

1. ✅ 设计完成 - HTTP API 接口
2. ✅ 实现完成 - Controller 和 HTTP 客户端
3. ⏳ 待修复 - InMemoryMatchEngineService 编译错误
4. ⏳ 待实现 - 修改 TradingService 使用 HttpMatchEngineClient
5. ⏳ 待删除 - 清理旧的 Channel 相关代码
6. ⏳ 待测试 - 启动两个服务测试完整流程

## 测试清单

- [ ] 撮合引擎单独启动成功
- [ ] 撮合引擎健康检查 `GET /api/match/health` 返回 200
- [ ] API 服务启动成功，能连接到撮合引擎
- [ ] 下单流程: API → MatchEngine → 撮合 → 返回结果
- [ ] 取消订单流程
- [ ] 查询资产流程
- [ ] 撮合引擎关闭时数据持久化成功
- [ ] 撮合引擎重启后数据恢复正常
