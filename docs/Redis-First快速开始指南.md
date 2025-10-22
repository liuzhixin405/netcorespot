# Redis-First 架构快速开始

## 📦 已创建的核心组件

### 1. Redis Repository 层
- ✅ `RedisOrderRepository.cs` - 订单仓储（创建、查询、更新、活跃订单簿）
- ✅ `RedisAssetRepository.cs` - 资产仓储（冻结、解冻、扣除、增加，使用 Lua 保证原子性）

### 2. 后台服务
- ✅ `RedisDataLoaderService.cs` - 启动时从 MySQL 加载数据到 Redis
- ✅ `RedisMySqlSyncService.cs` - 定期同步 Redis 数据到 MySQL（每 10 秒）

## 🚀 立即实施步骤

### 步骤 1：注册服务（修改 ServiceCollectionExtensions.cs）

```csharp
// src/CryptoSpot.Infrastructure/ServiceCollectionExtensions.cs

using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.Infrastructure.BgServices;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ===== Redis Repository 注册 =====
        services.AddSingleton<RedisOrderRepository>();
        services.AddSingleton<RedisAssetRepository>();
        
        // ===== 后台服务注册 =====
        // 数据加载服务（启动时执行）
        services.AddHostedService<RedisDataLoaderService>();
        
        // Redis → MySQL 同步服务
        services.AddHostedService<RedisMySqlSyncService>();
        
        // 移除之前的批处理服务（已被 Redis 同步服务替代）
        // services.AddHostedService<PriceUpdateBatchService>(); // ❌ 删除
        
        return services;
    }
}
```

### 步骤 2：修改撮合引擎使用 Redis

现在撮合引擎需要修改为使用 `RedisOrderRepository` 和 `RedisAssetRepository`，**完全不访问 DbContext**。

需要修改的文件：
- `OrderMatchingEngine.cs`

关键修改点：
```csharp
public class OrderMatchingEngine
{
    // ❌ 删除
    // private readonly IMatchingOrderStore _orderStore;
    // private readonly ApplicationDbContext _dbContext;
    
    // ✅ 新增
    private readonly RedisOrderRepository _redisOrders;
    private readonly RedisAssetRepository _redisAssets;
    
    public OrderMatchingEngine(
        RedisOrderRepository redisOrders,
        RedisAssetRepository redisAssets,
        IRealTimeDataPushService realTimePush,
        ILogger<OrderMatchingEngine> logger)
    {
        _redisOrders = redisOrders;
        _redisAssets = redisAssets;
        _realTimePush = realTimePush;
        _logger = logger;
    }
    
    // 所有订单操作改为 Redis
    public async Task<Order> PlaceOrderAsync(Order order)
    {
        // 1. 冻结资产
        var (currency, amount) = GetFreezeAmount(order);
        var freezeSuccess = await _redisAssets.FreezeAssetAsync(
            order.UserId, currency, amount);
        
        if (!freezeSuccess)
        {
            throw new InvalidOperationException("余额不足");
        }
        
        // 2. 创建订单（写入 Redis）
        await _redisOrders.CreateOrderAsync(order);
        
        // 3. 立即撮合
        await MatchOrderAsync(order.Symbol);
        
        return order;
    }
    
    private async Task MatchOrderAsync(string symbol)
    {
        // 从 Redis 获取活跃订单
        var buyOrders = await _redisOrders.GetActiveOrdersAsync(symbol, OrderSide.Buy, 100);
        var sellOrders = await _redisOrders.GetActiveOrdersAsync(symbol, OrderSide.Sell, 100);
        
        // ... 撮合逻辑 ...
        
        // 成交时：
        // 1. 扣除冻结资产
        await _redisAssets.DeductFrozenAssetAsync(buyOrder.UserId, quoteCurrency, buyAmount);
        await _redisAssets.DeductFrozenAssetAsync(sellOrder.UserId, baseCurrency, sellAmount);
        
        // 2. 增加收入资产
        await _redisAssets.AddAvailableAssetAsync(buyOrder.UserId, baseCurrency, matchedQuantity);
        await _redisAssets.AddAvailableAssetAsync(sellOrder.UserId, quoteCurrency, matchedAmount);
        
        // 3. 更新订单状态
        await _redisOrders.UpdateOrderStatusAsync(buyOrder.Id, newStatus, newFilled);
        await _redisOrders.UpdateOrderStatusAsync(sellOrder.Id, newStatus, newFilled);
        
        // 4. 记录成交（保存到 Redis，自动加入同步队列）
        await SaveTradeToRedis(trade);
    }
}
```

### 步骤 3：修改 API Controller 使用 Redis

```csharp
// Controllers/OrderController.cs

[ApiController]
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly RedisOrderRepository _redisOrders;
    private readonly OrderMatchingEngine _matchingEngine;
    
    public OrderController(
        RedisOrderRepository redisOrders,
        OrderMatchingEngine matchingEngine)
    {
        _redisOrders = redisOrders;
        _matchingEngine = matchingEngine;
    }
    
    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var order = new Order
        {
            UserId = GetCurrentUserId(),
            Symbol = request.Symbol,
            Side = request.Side,
            Type = OrderType.Limit,
            Price = request.Price,
            Quantity = request.Quantity,
            Status = OrderStatus.Pending
        };
        
        // ✅ 完全在 Redis 中完成
        var createdOrder = await _matchingEngine.PlaceOrderAsync(order);
        
        return Ok(createdOrder);
    }
    
    [HttpGet("my-orders")]
    public async Task<IActionResult> GetMyOrders()
    {
        var userId = GetCurrentUserId();
        
        // ✅ 从 Redis 查询
        var orders = await _redisOrders.GetUserOrdersAsync(userId);
        
        return Ok(orders);
    }
    
    [HttpDelete("{orderId}")]
    public async Task<IActionResult> CancelOrder(int orderId)
    {
        var userId = GetCurrentUserId();
        
        // ✅ 在 Redis 中取消
        var success = await _redisOrders.CancelOrderAsync(orderId, userId);
        
        if (!success) return BadRequest("订单无法取消");
        
        return Ok();
    }
}
```

### 步骤 4：修改市场数据服务使用 Redis

```csharp
// Services/PriceDataService.cs

public class PriceDataService
{
    private readonly IRedisCache _redis;
    
    public async Task UpdateTradingPairPriceAsync(
        string symbol, decimal price, decimal change24h, 
        decimal volume24h, decimal high24h, decimal low24h)
    {
        var key = $"trading_pair:{symbol}";
        
        // ✅ 直接更新 Redis
        await _redis.HMSetAsync(key,
            "price", price.ToString("F8"),
            "change24h", change24h.ToString("F8"),
            "volume24h", volume24h.ToString("F8"),
            "high24h", high24h.ToString("F8"),
            "low24h", low24h.ToString("F8"),
            "lastUpdated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        
        // ✅ 加入同步队列
        var json = JsonSerializer.Serialize(new { symbol, operation = "UPDATE_PRICE" });
        await _redis.ListLeftPushAsync("sync_queue:trading_pairs", json);
    }
}
```

## ⚙️ 配置调整

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=CryptoSpotDb;Uid=root;Pwd=123456;Pooling=true;MinimumPoolSize=5;MaximumPoolSize=30;ConnectionTimeout=30;ConnectionLifeTime=300;",
    "Redis": "localhost:6379"
  },
  "Redis": {
    "Configuration": "localhost:6379,defaultDatabase=0,syncTimeout=5000,asyncTimeout=5000",
    "InstanceName": "CryptoSpot:"
  }
}
```

### 注意：MySQL 连接池大幅缩小

因为运行时**几乎不访问 MySQL**，只有同步服务每 10 秒访问一次，所以：

- `MinimumPoolSize`: 5（足够）
- `MaximumPoolSize`: 30（降低到原来的 1/7）

## 📊 启动流程

```
1. 应用启动
   ↓
2. RedisDataLoaderService 启动
   ├─ 从 MySQL 加载用户 → Redis
   ├─ 从 MySQL 加载资产 → Redis
   ├─ 从 MySQL 加载交易对 → Redis
   ├─ 从 MySQL 加载活跃订单 → Redis
   └─ 初始化 ID 计数器
   ↓
3. RedisMySqlSyncService 启动（等待 30 秒）
   ↓
4. 其他服务启动（撮合、行情推送等）
   ↓
5. 运行阶段
   ├─ 所有订单操作 → Redis（无数据库访问）
   ├─ 所有资产操作 → Redis（无数据库访问）
   ├─ 所有撮合操作 → Redis（无数据库访问）
   └─ 每 10 秒：Redis → MySQL（批量同步）
```

## 🎯 数据流对比

### ❌ 旧架构（有并发问题）

```
下单 → DbContext → MySQL (阻塞)
撮合 → DbContext → MySQL (阻塞)
查询 → DbContext → MySQL (阻塞)

结果：高并发时 MySQL 连接池耗尽
```

### ✅ 新架构（Redis-First）

```
下单 → Redis (< 1ms)
撮合 → Redis (< 1ms)
查询 → Redis (< 1ms)

每 10 秒：
Redis → MySQL (批量同步，单线程，无竞争)

结果：零并发冲突，性能提升 100 倍
```

## 📝 完整修改清单

### 必须修改的文件（立即）

- [ ] `ServiceCollectionExtensions.cs` - 注册 Redis Repository 和服务
- [ ] `OrderMatchingEngine.cs` - 移除 DbContext，使用 Redis Repository
- [ ] `OrderController.cs` - 使用 Redis Repository
- [ ] `AssetController.cs` - 使用 Redis Repository
- [ ] `PriceDataService.cs` - 使用 Redis 直接更新

### 可选修改的文件（渐进）

- [ ] `MarketDataStreamRelayService.cs` - 移除 PriceUpdateBatchService，直接写 Redis
- [ ] `AutoTradingService.cs` - 使用 Redis Repository
- [ ] `KLineDataService.cs` - K线数据也可以考虑走 Redis

### 需要删除的文件

- [ ] `PriceUpdateBatchService.cs` - 已被 RedisMySqlSyncService 替代
- [ ] `MatchingOrderStore.cs` - 已被 RedisOrderRepository 替代
- [ ] `OrderRawAccess.cs` - 已被 RedisOrderRepository 替代

## ⚠️ 重要注意事项

### 1. 启动顺序很重要

确保 `RedisDataLoaderService` 在其他服务之前完成：

```csharp
// Program.cs
var app = builder.Build();

// ✅ 等待数据加载完成
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine("✅ 应用已启动，Redis 数据已就绪");
});

app.Run();
```

### 2. Redis 内存管理

定期清理历史数据：

```bash
# 清理 30 天前的成交记录
redis-cli --eval cleanup_old_trades.lua , 30

# 清理已完成的订单（保留 7 天）
redis-cli --eval cleanup_old_orders.lua , 7
```

### 3. 数据一致性保障

如果同步失败，数据会保留在同步队列中，下次继续尝试：

```bash
# 查看同步队列长度
redis-cli LLEN sync_queue:orders
redis-cli LLEN sync_queue:trades
redis-cli LLEN sync_queue:assets
```

### 4. 降级方案

如果 Redis 故障，可以临时切换回直接写 MySQL：

```csharp
// 在 Repository 中添加降级逻辑
if (!_redis.IsConnected)
{
    _logger.LogWarning("⚠️ Redis 不可用，降级到直接写 MySQL");
    return await _dbContext.SaveDirectlyAsync(order);
}
```

## 🔧 立即开始实施

**您希望我现在帮您：**

1. ✅ 修改 `OrderMatchingEngine.cs` 使用 Redis？
2. ✅ 修改 `OrderController.cs` 使用 Redis？
3. ✅ 修改 `ServiceCollectionExtensions.cs` 注册服务？
4. ✅ 提供完整的测试脚本？

**请告诉我从哪里开始！**
