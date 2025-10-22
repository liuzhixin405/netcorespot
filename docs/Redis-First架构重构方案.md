# Redis-First 架构重构方案

## 🎯 核心目标

**彻底分离 Redis 和 MySQL 访问，避免交易过程中的数据库连接冲突**

### 架构原则

1. ✅ **启动阶段**：MySQL → Redis（一次性全量加载）
2. ✅ **运行阶段**：所有操作只访问 Redis（零数据库访问）
3. ✅ **同步阶段**：Redis → MySQL（定期批量回写）

## 📊 数据流向

```
启动阶段:
MySQL ──读取──> Redis
  ├─ Users (用户数据)
  ├─ Assets (资产数据)
  ├─ Orders (活跃订单)
  ├─ TradingPairs (交易对)
  └─ Trades (最近成交，可选)

运行阶段:
OKX/Binance ──推送──> Redis ──读写──> 撮合引擎
                       │
                       └──> SignalR 推送

同步阶段 (每 10 秒):
Redis ──批量写入──> MySQL
  ├─ 新订单
  ├─ 订单状态更新
  ├─ 成交记录
  ├─ 资产变更
  └─ K线数据
```

## 🏗️ Redis 数据结构设计

### 1. 用户数据 (Hash)

```
Key: user:{userId}
Fields:
  - id: {userId}
  - username: "test_user_1"
  - passwordHash: "..."
  - email: "..."
  - createdAt: {timestamp}
```

### 2. 资产数据 (Hash)

```
Key: asset:{userId}:{currency}
Fields:
  - userId: {userId}
  - currency: "USDT"
  - availableBalance: 10000.00
  - frozenBalance: 500.00
  - totalBalance: 10500.00
  - updatedAt: {timestamp}
  
索引 Key: user_assets:{userId}
Type: Set
Members: ["USDT", "BTC", "ETH", ...]
```

### 3. 订单数据 (Hash + Sorted Set)

```
Key: order:{orderId}
Fields:
  - id: {orderId}
  - userId: {userId}
  - symbol: "BTCUSDT"
  - side: "Buy"
  - type: "Limit"
  - price: 50000.00
  - quantity: 0.1
  - filledQuantity: 0.05
  - status: "PartiallyFilled"
  - createdAt: {timestamp}
  - updatedAt: {timestamp}

索引 Key: orders:active:{symbol} (按价格排序的活跃订单)
Type: Sorted Set
Score: price (买单负数，卖单正数)
Member: orderId

索引 Key: user_orders:{userId}
Type: Set
Members: [orderId1, orderId2, ...]
```

### 4. 交易对数据 (Hash)

```
Key: trading_pair:{symbol}
Fields:
  - symbol: "BTCUSDT"
  - baseCurrency: "BTC"
  - quoteCurrency: "USDT"
  - price: 50000.00
  - change24h: 0.0526
  - volume24h: 1234567.89
  - high24h: 51000.00
  - low24h: 48500.00
  - lastUpdated: {timestamp}
```

### 5. 成交记录 (List + Hash)

```
Key: trade:{tradeId}
Fields:
  - id: {tradeId}
  - symbol: "BTCUSDT"
  - buyOrderId: {orderId}
  - sellOrderId: {orderId}
  - price: 50000.00
  - quantity: 0.1
  - buyerUserId: {userId}
  - sellerUserId: {userId}
  - timestamp: {timestamp}

索引 Key: trades:{symbol} (最近成交)
Type: List
Members: [tradeId1, tradeId2, ...] (最多保留 1000 条)

索引 Key: user_trades:{userId}
Type: List
Members: [tradeId1, tradeId2, ...]
```

### 6. 待同步数据队列 (List)

```
Key: sync_queue:orders
Type: List
Members: [{orderId, operation, timestamp}, ...]

Key: sync_queue:trades
Type: List
Members: [{tradeId, operation, timestamp}, ...]

Key: sync_queue:assets
Type: List
Members: [{userId, currency, operation, timestamp}, ...]
```

## 🔧 核心服务实现

### 1. Redis 数据加载服务

```csharp
public class RedisDataLoaderService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRedisCache _redis;
    private readonly ILogger<RedisDataLoaderService> _logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 开始从 MySQL 加载数据到 Redis...");
        
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        // 1. 加载用户数据
        await LoadUsersAsync(dbContext);
        
        // 2. 加载资产数据
        await LoadAssetsAsync(dbContext);
        
        // 3. 加载交易对数据
        await LoadTradingPairsAsync(dbContext);
        
        // 4. 加载活跃订单
        await LoadActiveOrdersAsync(dbContext);
        
        // 5. 加载最近成交（可选）
        await LoadRecentTradesAsync(dbContext);
        
        _logger.LogInformation("✅ 数据加载完成！");
    }
    
    private async Task LoadUsersAsync(ApplicationDbContext db)
    {
        var users = await db.Users.ToListAsync();
        foreach (var user in users)
        {
            await _redis.HMSetAsync($"user:{user.Id}", 
                "id", user.Id,
                "username", user.Username,
                "passwordHash", user.PasswordHash,
                "email", user.Email ?? "",
                "createdAt", user.CreatedAt.ToString("O"));
        }
        _logger.LogInformation($"✅ 加载 {users.Count} 个用户");
    }
    
    private async Task LoadAssetsAsync(ApplicationDbContext db)
    {
        var assets = await db.Assets.ToListAsync();
        foreach (var asset in assets)
        {
            var key = $"asset:{asset.UserId}:{asset.Currency}";
            await _redis.HMSetAsync(key,
                "userId", asset.UserId,
                "currency", asset.Currency,
                "availableBalance", asset.AvailableBalance.ToString(),
                "frozenBalance", asset.FrozenBalance.ToString(),
                "totalBalance", (asset.AvailableBalance + asset.FrozenBalance).ToString(),
                "updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            
            // 添加到用户资产索引
            await _redis.Execute("SADD", $"user_assets:{asset.UserId}", asset.Currency);
        }
        _logger.LogInformation($"✅ 加载 {assets.Count} 条资产记录");
    }
    
    private async Task LoadTradingPairsAsync(ApplicationDbContext db)
    {
        var pairs = await db.TradingPairs.ToListAsync();
        foreach (var pair in pairs)
        {
            await _redis.HMSetAsync($"trading_pair:{pair.Symbol}",
                "symbol", pair.Symbol,
                "baseCurrency", pair.BaseCurrency,
                "quoteCurrency", pair.QuoteCurrency,
                "price", pair.Price.ToString(),
                "change24h", pair.Change24h.ToString(),
                "volume24h", pair.Volume24h.ToString(),
                "high24h", pair.High24h.ToString(),
                "low24h", pair.Low24h.ToString(),
                "lastUpdated", pair.LastUpdated.ToString());
        }
        _logger.LogInformation($"✅ 加载 {pairs.Count} 个交易对");
    }
    
    private async Task LoadActiveOrdersAsync(ApplicationDbContext db)
    {
        var activeOrders = await db.Orders
            .Where(o => o.Status == OrderStatus.Open || o.Status == OrderStatus.PartiallyFilled || o.Status == OrderStatus.Pending)
            .ToListAsync();
            
        foreach (var order in activeOrders)
        {
            await SaveOrderToRedis(order);
        }
        _logger.LogInformation($"✅ 加载 {activeOrders.Count} 条活跃订单");
    }
}
```

### 2. Redis Repository 实现

```csharp
public class RedisOrderRepository
{
    private readonly IRedisCache _redis;
    private readonly ILogger<RedisOrderRepository> _logger;
    
    // 创建订单
    public async Task<Order> CreateOrderAsync(Order order)
    {
        var orderId = await GenerateOrderIdAsync();
        order.Id = orderId;
        
        // 保存订单数据
        await _redis.HMSetAsync($"order:{orderId}",
            "id", orderId,
            "userId", order.UserId,
            "symbol", order.Symbol,
            "side", order.Side.ToString(),
            "type", order.Type.ToString(),
            "price", order.Price.ToString(),
            "quantity", order.Quantity.ToString(),
            "filledQuantity", order.FilledQuantity.ToString(),
            "status", order.Status.ToString(),
            "createdAt", order.CreatedAt.ToString("O"),
            "updatedAt", order.UpdatedAt.ToString("O"));
        
        // 添加到索引
        await _redis.Execute("SADD", $"user_orders:{order.UserId}", orderId);
        
        // 添加到活跃订单簿
        if (order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Filled)
        {
            var score = order.Side == OrderSide.Buy ? -order.Price : order.Price;
            await _redis.Execute("ZADD", $"orders:active:{order.Symbol}", score, orderId);
        }
        
        // 加入同步队列
        await AddToSyncQueue("orders", new { orderId, operation = "CREATE" });
        
        return order;
    }
    
    // 获取活跃订单
    public async Task<List<Order>> GetActiveOrdersAsync(string symbol, OrderSide side)
    {
        var key = $"orders:active:{symbol}";
        var orderIds = side == OrderSide.Buy
            ? await _redis.SortedSetRangeByScoreAsync(key, double.NegativeInfinity, 0, false) // 降序
            : await _redis.SortedSetRangeByScoreAsync(key, 0, double.PositiveInfinity, true); // 升序
        
        var orders = new List<Order>();
        foreach (var orderId in orderIds)
        {
            var order = await GetOrderByIdAsync(int.Parse(orderId));
            if (order != null) orders.Add(order);
        }
        return orders;
    }
    
    // 更新订单状态
    public async Task UpdateOrderStatusAsync(int orderId, OrderStatus newStatus, decimal filledQuantity)
    {
        await _redis.HMSetAsync($"order:{orderId}",
            "status", newStatus.ToString(),
            "filledQuantity", filledQuantity.ToString(),
            "updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        
        // 如果订单完成或取消，从活跃订单簿移除
        if (newStatus == OrderStatus.Filled || newStatus == OrderStatus.Cancelled)
        {
            var symbol = await _redis.HGetAsync<string>($"order:{orderId}", "symbol");
            await _redis.Execute("ZREM", $"orders:active:{symbol}", orderId);
        }
        
        // 加入同步队列
        await AddToSyncQueue("orders", new { orderId, operation = "UPDATE" });
    }
}
```

### 3. Redis Asset Repository

```csharp
public class RedisAssetRepository
{
    private readonly IRedisCache _redis;
    
    // 冻结资产
    public async Task<bool> FreezeAssetAsync(int userId, string currency, decimal amount)
    {
        var key = $"asset:{userId}:{currency}";
        
        // Lua 脚本确保原子性
        var script = @"
            local available = tonumber(redis.call('HGET', KEYS[1], 'availableBalance'))
            local frozen = tonumber(redis.call('HGET', KEYS[1], 'frozenBalance'))
            local amount = tonumber(ARGV[1])
            
            if available >= amount then
                redis.call('HSET', KEYS[1], 'availableBalance', available - amount)
                redis.call('HSET', KEYS[1], 'frozenBalance', frozen + amount)
                redis.call('HSET', KEYS[1], 'updatedAt', ARGV[2])
                return 1
            else
                return 0
            end
        ";
        
        var result = await _redis.ScriptEvaluateAsync(
            LuaScript.Prepare(script),
            new { key, amount, timestamp = DateTimeOffset.UtcNow.ToString("O") });
        
        if ((int)result == 1)
        {
            await AddToSyncQueue("assets", new { userId, currency, operation = "FREEZE", amount });
            return true;
        }
        return false;
    }
    
    // 解冻并扣除资产（成交时）
    public async Task<bool> UnfreezeAndDeductAsync(int userId, string currency, decimal amount)
    {
        var key = $"asset:{userId}:{currency}";
        
        var script = @"
            local frozen = tonumber(redis.call('HGET', KEYS[1], 'frozenBalance'))
            local amount = tonumber(ARGV[1])
            
            if frozen >= amount then
                redis.call('HSET', KEYS[1], 'frozenBalance', frozen - amount)
                redis.call('HSET', KEYS[1], 'updatedAt', ARGV[2])
                return 1
            else
                return 0
            end
        ";
        
        var result = await _redis.ScriptEvaluateAsync(
            LuaScript.Prepare(script),
            new { key, amount, timestamp = DateTimeOffset.UtcNow.ToString("O") });
        
        if ((int)result == 1)
        {
            await AddToSyncQueue("assets", new { userId, currency, operation = "DEDUCT", amount });
            return true;
        }
        return false;
    }
    
    // 增加资产（成交收入）
    public async Task AddAssetAsync(int userId, string currency, decimal amount)
    {
        var key = $"asset:{userId}:{currency}";
        await _redis.Hincrby(key, "availableBalance", (long)(amount * 100000000)); // 精度转换
        await _redis.HMSetAsync(key, "updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        
        await AddToSyncQueue("assets", new { userId, currency, operation = "ADD", amount });
    }
}
```

### 4. MySQL 同步服务

```csharp
public class RedisMySqlSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRedisCache _redis;
    private readonly ILogger<RedisMySqlSyncService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("✅ Redis → MySQL 同步服务已启动");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10000, stoppingToken); // 每 10 秒同步一次
                
                await SyncOrdersAsync();
                await SyncTradesAsync();
                await SyncAssetsAsync();
                
                _logger.LogDebug("✅ 同步周期完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 同步失败");
            }
        }
    }
    
    private async Task SyncOrdersAsync()
    {
        var queue = "sync_queue:orders";
        var count = await _redis.ListLengthAsync(queue);
        
        if (count == 0) return;
        
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        // 批量获取待同步订单
        var items = await _redis.ListRightPopAsync<dynamic>(queue, (int)Math.Min(count, 100));
        
        foreach (var item in items)
        {
            var orderId = (int)item.orderId;
            var operation = (string)item.operation;
            
            var orderData = await _redis.HGetAllAsync($"order:{orderId}");
            
            if (operation == "CREATE")
            {
                var order = MapToOrder(orderData);
                dbContext.Orders.Add(order);
            }
            else if (operation == "UPDATE")
            {
                var order = await dbContext.Orders.FindAsync(orderId);
                if (order != null)
                {
                    order.Status = Enum.Parse<OrderStatus>(orderData["status"]);
                    order.FilledQuantity = decimal.Parse(orderData["filledQuantity"]);
                    order.UpdatedAt = DateTimeOffset.Parse(orderData["updatedAt"]);
                }
            }
        }
        
        await dbContext.SaveChangesAsync();
        _logger.LogInformation($"✅ 同步 {items.Count} 条订单记录");
    }
    
    private async Task SyncTradesAsync()
    {
        // 类似实现...
    }
    
    private async Task SyncAssetsAsync()
    {
        // 类似实现...
    }
}
```

## 📝 实施步骤

### 阶段 1：创建 Redis Repository（1-2 小时）

1. 创建 `RedisOrderRepository.cs`
2. 创建 `RedisAssetRepository.cs`
3. 创建 `RedisTradeRepository.cs`
4. 创建 `RedisTradingPairRepository.cs`

### 阶段 2：创建数据加载服务（30 分钟）

1. 创建 `RedisDataLoaderService.cs`
2. 注册为 `IHostedService`

### 阶段 3：创建同步服务（30 分钟）

1. 创建 `RedisMySqlSyncService.cs`
2. 注册为 `BackgroundService`

### 阶段 4：修改撮合引擎（1 小时）

1. 修改 `OrderMatchingEngine` 使用 Redis Repository
2. 移除所有 DbContext 依赖

### 阶段 5：修改市场数据服务（30 分钟）

1. 修改 `MarketDataStreamRelayService` 使用 Redis
2. 修改 `PriceUpdateBatchService` 使用 Redis

### 阶段 6：测试验证（1 小时）

1. 启动测试
2. 性能测试
3. 数据一致性验证

## ⚠️ 注意事项

1. **ID 生成**：使用 Redis INCR 生成全局唯一 ID
2. **原子性**：关键操作使用 Lua 脚本保证原子性
3. **数据一致性**：同步失败时记录日志，支持重试
4. **内存管理**：定期清理 Redis 中的历史数据
5. **降级方案**：Redis 故障时可降级到直接写 MySQL

## 📊 性能预期

| 指标 | 当前架构 | Redis-First 架构 |
|------|----------|------------------|
| 订单创建 TPS | 100-200 | **5000-10000** |
| 撮合延迟 | 50-100ms | **< 5ms** |
| 数据库连接数 | 50-200 | **< 10** |
| 并发错误率 | 5-10% | **0%** |
| 内存使用 | 200MB | **500MB-1GB** |

## 🎯 下一步

**立即开始实施吗？我可以：**

1. 创建所有 Redis Repository 实现
2. 创建数据加载和同步服务
3. 修改撮合引擎使用 Redis
4. 提供完整的测试方案

**请确认是否开始实施？**
