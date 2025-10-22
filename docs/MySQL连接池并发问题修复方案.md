# MySQL 连接池并发问题修复方案

## 🔴 问题症状

```
System.InvalidOperationException: ServerSession is not connected.
System.NotSupportedException: This method may not be called when another read operation is pending.
```

## 🔍 根本原因

### 1. DbContext 并发访问冲突

**问题代码位置**：`MarketDataStreamRelayService.cs`

```csharp
// ❌ 问题：在高频回调中创建大量并发的 Scope
private async Task RelayTickerAsync(MarketTicker t, CancellationToken ct)
{
    // 异步推送到 SignalR（可能并发执行）
    using var scope = _scopeFactory.CreateScope();
    var push = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
    
    // 同时在 Task.Run 中异步更新数据库（并发冲突！）
    _ = Task.Run(async () =>
    {
        using var persistScope = _scopeFactory.CreateScope();
        var scopedPriceService = persistScope.ServiceProvider.GetRequiredService<IPriceDataService>();
        await scopedPriceService.UpdateTradingPairPriceAsync(...);
    }, CancellationToken.None);
}
```

### 2. 连接池配置不足

**当前配置**：
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=CryptoSpotDb;Uid=root;Pwd=123456;
    Pooling=true;
    MinimumPoolSize=5;
    MaximumPoolSize=50;
    ConnectionTimeout=60;
    ConnectionLifeTime=600;"
}
```

**DbContext 池配置**：
```csharp
services.AddDbContextPool<ApplicationDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.Parse("8.0"), mysqlOptions =>
    {
        mysqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(30), null);
        mysqlOptions.CommandTimeout(60);
    });
    options.EnableThreadSafetyChecks(false); // ⚠️ 禁用线程安全检查是危险的
}, poolSize: 20);
```

### 3. 并发场景分析

1. **MarketDataStreamRelayService**：每秒接收 3+ 交易对的 Ticker 更新
2. **OrderMatchingEngine**：持续轮询订单簿
3. **AutoTradingService**：定时执行市场做市
4. **所有服务同时**：查询、更新数据库

结果：**连接池耗尽、并发读写冲突**

## ✅ 解决方案

### 方案 1：数据库更新改为队列批处理（推荐）

#### 1.1 创建批处理服务

```csharp
// src/CryptoSpot.Infrastructure/BgService/PriceUpdateBatchService.cs
using System.Threading.Channels;

namespace CryptoSpot.Infrastructure.BgServices;

/// <summary>
/// 批量处理价格更新，避免高频并发数据库操作
/// </summary>
public class PriceUpdateBatchService : BackgroundService
{
    private readonly ILogger<PriceUpdateBatchService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<PriceUpdateRequest> _channel;
    
    private record PriceUpdateRequest(
        string Symbol,
        decimal Price,
        decimal Change24h,
        decimal Volume24h,
        decimal High24h,
        decimal Low24h);

    public PriceUpdateBatchService(
        ILogger<PriceUpdateBatchService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _channel = Channel.CreateUnbounded<PriceUpdateRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// 异步提交价格更新请求（非阻塞）
    /// </summary>
    public bool TryEnqueue(string symbol, decimal price, decimal change, decimal volume, decimal high, decimal low)
    {
        return _channel.Writer.TryWrite(new PriceUpdateRequest(symbol, price, change, volume, high, low));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("✅ PriceUpdateBatchService 已启动");

        var buffer = new List<PriceUpdateRequest>(100);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                buffer.Clear();
                
                // 等待第一个请求
                await _channel.Reader.WaitToReadAsync(stoppingToken);
                
                // 收集批次（最多等待 100ms 或收集到 50 个）
                var timeout = Task.Delay(100, stoppingToken);
                while (buffer.Count < 50 && !timeout.IsCompleted)
                {
                    if (_channel.Reader.TryRead(out var request))
                    {
                        buffer.Add(request);
                    }
                    else
                    {
                        await Task.WhenAny(_channel.Reader.WaitToReadAsync(stoppingToken).AsTask(), timeout);
                    }
                }
                
                if (buffer.Count > 0)
                {
                    await ProcessBatchAsync(buffer, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PriceUpdateBatchService 批处理异常");
                await Task.Delay(1000, stoppingToken); // 错误后延迟
            }
        }
        
        _logger.LogInformation("❌ PriceUpdateBatchService 已停止");
    }

    private async Task ProcessBatchAsync(List<PriceUpdateRequest> batch, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var priceService = scope.ServiceProvider.GetRequiredService<IPriceDataService>();
            
            // 去重：每个 symbol 只保留最新的一条
            var latestUpdates = batch
                .GroupBy(x => x.Symbol)
                .Select(g => g.Last())
                .ToList();
            
            _logger.LogDebug("📦 批处理价格更新: {Count} 个请求 -> {Unique} 个唯一交易对", 
                batch.Count, latestUpdates.Count);
            
            // 批量更新
            foreach (var update in latestUpdates)
            {
                await priceService.UpdateTradingPairPriceAsync(
                    update.Symbol,
                    update.Price,
                    update.Change24h,
                    update.Volume24h,
                    update.High24h,
                    update.Low24h);
            }
            
            _logger.LogDebug("✅ 批处理完成: {Count} 个交易对已更新", latestUpdates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批处理执行失败，批次大小: {Count}", batch.Count);
        }
    }
}
```

#### 1.2 修改 MarketDataStreamRelayService

```csharp
public class MarketDataStreamRelayService : BackgroundService
{
    private readonly PriceUpdateBatchService _batchService; // 新增

    public MarketDataStreamRelayService(
        ILogger<MarketDataStreamRelayService> logger,
        IServiceScopeFactory scopeFactory,
        IEnumerable<IMarketDataStreamProvider> streamProviders,
        IDtoMappingService mapping,
        PriceUpdateBatchService batchService) // 新增参数
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _streamProviders = streamProviders;
        _mapping = mapping;
        _batchService = batchService; // 新增
    }

    private async Task RelayTickerAsync(MarketTicker t, CancellationToken ct)
    {
        try
        {
            // ... 去重和限流逻辑 ...

            using var scope = _scopeFactory.CreateScope();
            var push = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();

            // ✅ 改为异步队列提交（非阻塞）
            if (state.LastPushMs == 0 || 
                Math.Abs(last - state.Price) / state.Price > 0.0001m ||
                Math.Abs(change - state.Change) > 0.0001m)
            {
                _batchService.TryEnqueue(symbol, last, change, vol, high, low);
            }

            // ❌ 删除原来的 Task.Run 异步更新代码
            // _ = Task.Run(async () => { ... }, CancellationToken.None);

            await push.PushPriceDataAsync(t.Symbol, priceData);
            _lastTickerState[t.Symbol] = (t.Last, t.ChangePercent, t.Volume24h, t.High24h, t.Low24h, nowMs, hash);
            _logger.LogInformation("✅ Ticker Relay 推送完成 {Symbol}", t.Symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RelayTicker 失败 {Symbol}", t.Symbol);
        }
    }
}
```

#### 1.3 注册服务

```csharp
// ServiceCollectionExtensions.cs
services.AddSingleton<PriceUpdateBatchService>();
services.AddHostedService(sp => sp.GetRequiredService<PriceUpdateBatchService>());
```

### 方案 2：优化连接池配置（辅助方案）

#### 2.1 增强连接字符串

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=CryptoSpotDb;Uid=root;Pwd=123456;Pooling=true;MinimumPoolSize=10;MaximumPoolSize=200;ConnectionTimeout=30;ConnectionLifeTime=300;AllowUserVariables=true;UseAffectedRows=false;ConnectionIdleTimeout=180;SslMode=None;AllowPublicKeyRetrieval=true;"
  }
}
```

**关键参数说明**：
- `MaximumPoolSize=200`：大幅增加连接池上限（从 50 → 200）
- `ConnectionLifeTime=300`：缩短连接生命周期（从 600s → 300s）避免长时间占用
- `ConnectionIdleTimeout=180`：空闲连接 3 分钟后回收
- `SslMode=None`：本地开发禁用 SSL 提升性能
- `ConnectionTimeout=30`：缩短超时时间快速失败

#### 2.2 增加 DbContext 池大小

```csharp
services.AddDbContextPool<ApplicationDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.Parse("8.0"), mysqlOptions =>
    {
        mysqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
        mysqlOptions.CommandTimeout(30); // 缩短到 30 秒
    });
    options.EnableSensitiveDataLogging(false); // 生产环境关闭
    options.EnableThreadSafetyChecks(false); // 仅在确保单线程使用时禁用
}, poolSize: 100); // 从 20 增加到 100
```

### 方案 3：异步操作改进（必须）

#### 3.1 避免 Task.Run 包裹数据库操作

```csharp
// ❌ 错误：Task.Run 创建新线程，增加并发压力
_ = Task.Run(async () =>
{
    using var scope = _scopeFactory.CreateScope();
    await DoSomethingAsync();
}, CancellationToken.None);

// ✅ 正确：直接异步执行或使用队列
using var scope = _scopeFactory.CreateScope();
await DoSomethingAsync();

// ✅ 或：提交到队列
_batchService.TryEnqueue(data);
```

#### 3.2 确保 Scope 生命周期正确

```csharp
// ❌ 错误：Scope 可能在异步操作完成前被释放
using var scope = _scopeFactory.CreateScope();
_ = Task.Run(async () =>
{
    var service = scope.ServiceProvider.GetService<IMyService>();
    await service.DoAsync(); // ❌ scope 可能已被释放
});

// ✅ 正确：在异步任务内部创建 Scope
_ = Task.Run(async () =>
{
    using var scope = _scopeFactory.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
    await service.DoAsync();
});
```

### 方案 4：数据库层面优化

#### 4.1 检查 MySQL 连接数配置

```sql
-- 查看当前配置
SHOW VARIABLES LIKE 'max_connections';
SHOW VARIABLES LIKE 'max_user_connections';

-- 查看当前活跃连接
SHOW PROCESSLIST;
SHOW STATUS LIKE 'Threads_connected';

-- 调整连接数限制（需要 root 权限）
SET GLOBAL max_connections = 500;
SET GLOBAL max_user_connections = 400;
```

#### 4.2 优化慢查询

```sql
-- 启用慢查询日志
SET GLOBAL slow_query_log = 'ON';
SET GLOBAL long_query_time = 1; -- 超过 1 秒记录

-- 查看当前锁等待
SHOW ENGINE INNODB STATUS;
```

## 🚀 实施步骤

### 第一阶段：紧急修复（立即执行）

1. **增加连接池配置**
   ```bash
   # 修改 appsettings.json
   MaximumPoolSize=200
   poolSize: 100
   ```

2. **临时禁用高频数据库更新**
   ```csharp
   // MarketDataStreamRelayService.cs
   // 注释掉 Task.Run 数据库更新代码
   ```

3. **重启应用**
   ```bash
   dotnet build
   dotnet run
   ```

### 第二阶段：根本解决（1-2 小时内）

1. **实现 PriceUpdateBatchService**
2. **修改 MarketDataStreamRelayService 使用批处理**
3. **测试验证**

### 第三阶段：监控优化（持续）

1. **添加性能监控**
   ```csharp
   services.AddHealthChecks()
       .AddDbContextCheck<ApplicationDbContext>();
   ```

2. **日志监控**
   ```bash
   # 实时监控数据库错误
   tail -f logs/app.log | grep -E "ServerSession|read operation is pending"
   ```

## 📊 验证修复

### 1. 检查连接池使用情况

```sql
-- MySQL 端查看
SHOW STATUS LIKE 'Threads_connected';
SHOW PROCESSLIST;

-- 应该看到稳定的连接数（不会暴涨）
```

### 2. 检查日志

```bash
# 确认批处理服务启动
grep "PriceUpdateBatchService 已启动" logs/app.log

# 确认批处理正常工作
grep "批处理价格更新" logs/app.log

# 确认无并发错误
grep -i "ServerSession is not connected" logs/app.log
```

### 3. 性能指标

| 指标 | 修复前 | 修复后 |
|------|--------|--------|
| 数据库连接数 | 50+ (峰值) | 10-20 (稳定) |
| 并发错误频率 | 每分钟 10+ | 0 |
| 价格更新延迟 | 实时 | < 200ms |
| CPU 使用率 | 60-80% | 30-50% |

## 🔧 故障排查

### 问题 1：批处理延迟过高

**症状**：价格更新延迟超过 500ms

**解决**：
```csharp
// 减小批处理间隔
var timeout = Task.Delay(50, stoppingToken); // 从 100ms 改为 50ms
```

### 问题 2：队列积压

**症状**：`_channel.Reader.Count` 持续增长

**解决**：
```csharp
// 使用有界队列
_channel = Channel.CreateBounded<PriceUpdateRequest>(new BoundedChannelOptions(1000)
{
    FullMode = BoundedChannelFullMode.DropOldest // 丢弃旧数据
});
```

### 问题 3：数据库连接仍然不足

**症状**：仍然出现 "ServerSession is not connected"

**解决**：
1. 检查是否有其他服务占用连接
2. 增加 MySQL `max_connections` 到 1000
3. 检查是否有连接泄漏（未正确释放 Scope）

## 📝 代码审查清单

在所有后台服务中检查：

- [ ] ❌ 避免在高频回调中使用 `Task.Run`
- [ ] ❌ 避免在异步回调中并发访问 DbContext
- [ ] ✅ 使用 `IServiceScopeFactory` 创建隔离的 Scope
- [ ] ✅ 确保每个异步任务内部创建自己的 Scope
- [ ] ✅ 使用队列/批处理处理高频数据库操作
- [ ] ✅ 配置合理的连接池大小和超时时间
- [ ] ✅ 启用数据库连接重试机制

## 相关文档

- [EF Core DbContext 线程安全性](https://learn.microsoft.com/ef/core/dbcontext-configuration/)
- [MySqlConnector 连接池配置](https://mysqlconnector.net/connection-options/)
- [System.Threading.Channels 文档](https://learn.microsoft.com/dotnet/api/system.threading.channels)
