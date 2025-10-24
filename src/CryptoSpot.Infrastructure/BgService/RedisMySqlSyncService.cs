using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using CryptoSpot.Persistence.Data;
using CryptoSpot.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CryptoSpot.Infrastructure.BgService;

/// <summary>
/// Redis → MySQL 数据同步服务（定期批量同步）
/// </summary>
public class RedisMySqlSyncService : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IRedisCache _redis;
    private readonly ILogger<RedisMySqlSyncService> _logger;
    private const int SYNC_INTERVAL_SECONDS = 10; // 每 10 秒同步一次
    private const int BATCH_SIZE = 500; // 每批最多处理 500 条
    private const long PRECISION = 100000000; // 8 位小数精度

    public RedisMySqlSyncService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IRedisCache redis,
        ILogger<RedisMySqlSyncService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("✅ Redis → MySQL 同步服务已启动（间隔: {Interval}秒）", SYNC_INTERVAL_SECONDS);

        // 等待 30 秒让系统完全启动
        await Task.Delay(30000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var startTime = DateTimeOffset.UtcNow;

                // 同步订单
                var orderCount = await SyncOrdersAsync(stoppingToken);

                // 同步成交
                var tradeCount = await SyncTradesAsync(stoppingToken);

                // 同步资产
                var assetCount = await SyncAssetsAsync(stoppingToken);

                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

                if (orderCount > 0 || tradeCount > 0 || assetCount > 0)
                {
                    _logger.LogInformation(
                        "✅ 同步完成: 订单={Orders}, 成交={Trades}, 资产={Assets}, 耗时={Elapsed}ms",
                        orderCount, tradeCount, assetCount, elapsed);
                }

                await Task.Delay(SYNC_INTERVAL_SECONDS * 1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 同步失败");
                await Task.Delay(5000, stoppingToken); // 错误后等待 5 秒
            }
        }

        _logger.LogInformation("⏹️ RedisMySqlSyncService 已停止");
    }

    #region 同步订单

    private async Task<int> SyncOrdersAsync(CancellationToken ct)
    {
        var queueKey = "sync_queue:orders";
        var queueLength = await _redis.ListLengthAsync(queueKey);

        if (queueLength == 0) return 0;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);

        var batchSize = Math.Min((int)queueLength, BATCH_SIZE);
        var processedCount = 0;

        try
        {
            for (int i = 0; i < batchSize; i++)
            {
                var json = await _redis.ListRightPopAsync(queueKey);
                if (string.IsNullOrEmpty(json)) break;

                var item = JsonSerializer.Deserialize<SyncQueueItem>(json);
                if (item == null) continue;

                var orderId = item.orderId;
                var operation = item.operation;

                // 从 Redis 读取最新订单数据
                var orderData = await _redis.HGetAllAsync($"order:{orderId}");
                if (orderData == null || orderData.Count == 0) continue;

                if (operation == "CREATE")
                {
                    // 检查是否已存在（防止重复）
                    var exists = await dbContext.Orders.AnyAsync(o => o.Id == orderId, ct);
                    if (!exists)
                    {
                        var order = MapToOrder(orderData);
                        dbContext.Orders.Add(order);
                    }
                }
                else if (operation == "UPDATE")
                {
                    var order = await dbContext.Orders.FindAsync(new object[] { orderId }, ct);
                    if (order != null)
                    {
                        order.Status = (OrderStatus)int.Parse(orderData["status"]);
                        order.FilledQuantity = decimal.Parse(orderData["filledQuantity"]);
                        order.UpdatedAt = long.Parse(orderData["updatedAt"]); // ✅ long 不是 DateTimeOffset
                        dbContext.Orders.Update(order);
                    }
                }

                processedCount++;
            }

            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 同步订单失败");
        }

        return processedCount;
    }

    #endregion

    #region 同步成交

    private async Task<int> SyncTradesAsync(CancellationToken ct)
    {
        var queueKey = "sync_queue:trades";
        var queueLength = await _redis.ListLengthAsync(queueKey);

        if (queueLength == 0) return 0;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);

        var batchSize = Math.Min((int)queueLength, BATCH_SIZE);
        var processedCount = 0;

        try
        {
            for (int i = 0; i < batchSize; i++)
            {
                var json = await _redis.ListRightPopAsync(queueKey);
                if (string.IsNullOrEmpty(json)) break;

                var item = JsonSerializer.Deserialize<SyncQueueItem>(json);
                if (item == null) continue;

                var tradeId = item.tradeId ?? 0;
                if (tradeId == 0) continue;

                // 从 Redis 读取最新成交数据
                var tradeData = await _redis.HGetAllAsync($"trade:{tradeId}");
                if (tradeData == null || tradeData.Count == 0) continue;

                // 检查是否已存在
                var exists = await dbContext.Trades.AnyAsync(t => t.Id == tradeId, ct);
                if (!exists)
                {
                    var trade = MapToTrade(tradeData);
                    dbContext.Trades.Add(trade);
                    processedCount++;
                }
            }

            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 同步成交失败");
        }

        return processedCount;
    }

    #endregion

    #region 同步资产

    private async Task<int> SyncAssetsAsync(CancellationToken ct)
    {
        var queueKey = "sync_queue:assets";
        var queueLength = await _redis.ListLengthAsync(queueKey);

        if (queueLength == 0) return 0;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);

        var batchSize = Math.Min((int)queueLength, BATCH_SIZE);
        var processedCount = 0;

        // 用于批量更新资产（去重）
        var assetsToUpdate = new Dictionary<string, (int userId, string currency)>();

        try
        {
            // 收集需要更新的资产
            for (int i = 0; i < batchSize; i++)
            {
                var json = await _redis.ListRightPopAsync(queueKey);
                if (string.IsNullOrEmpty(json)) break;

                var item = JsonSerializer.Deserialize<SyncQueueItem>(json);
                if (item == null) continue;

                var key = $"{item.userId}:{item.symbol}"; // ✅ symbol 不是 currency
                assetsToUpdate[key] = (item.userId, item.symbol); // ✅ symbol 不是 currency
            }

            // 批量从 Redis 读取最新资产数据并更新到 MySQL
            foreach (var (key, (userId, symbol)) in assetsToUpdate) // ✅ symbol 不是 currency
            {
                var assetData = await _redis.HGetAllAsync($"asset:{userId}:{symbol}");
                if (assetData == null || assetData.Count == 0) continue;

                var asset = await dbContext.Assets.FirstOrDefaultAsync(
                    a => a.UserId == userId && a.Symbol == symbol, ct); // ✅ Symbol 不是 Currency

                var available = (decimal)long.Parse(assetData["availableBalance"]) / PRECISION;
                var frozen = (decimal)long.Parse(assetData["frozenBalance"]) / PRECISION;

                if (asset == null)
                {
                    // 创建新资产
                    asset = new Asset
                    {
                        UserId = userId,
                        Symbol = symbol,              // ✅ Symbol 不是 Currency
                        Available = available,        // ✅ Available 不是 AvailableBalance
                        Frozen = frozen,              // ✅ Frozen 不是 FrozenBalance
                        CreatedAt = long.Parse(assetData["createdAt"]),  // ✅ long 不是 DateTimeOffset
                        UpdatedAt = long.Parse(assetData["updatedAt"])   // ✅ long 不是 DateTimeOffset
                    };
                    dbContext.Assets.Add(asset);
                }
                else
                {
                    // 更新现有资产
                    asset.Available = available;      // ✅ Available 不是 AvailableBalance
                    asset.Frozen = frozen;            // ✅ Frozen 不是 FrozenBalance
                    asset.UpdatedAt = long.Parse(assetData["updatedAt"]); // ✅ long 不是 DateTimeOffset
                    dbContext.Assets.Update(asset);
                }

                processedCount++;
            }

            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 同步资产失败");
        }

        return processedCount;
    }

    #endregion

    #region 辅助方法

    private Order MapToOrder(Dictionary<string, string> data)
    {
        return new Order
        {
            Id = int.Parse(data["id"]),
            UserId = int.Parse(data["userId"]),
            TradingPairId = int.Parse(data["tradingPairId"]), // ✅ 使用 TradingPairId 不是 Symbol
            Side = (OrderSide)int.Parse(data["side"]),
            Type = (OrderType)int.Parse(data["type"]),
            Price = decimal.Parse(data["price"]),
            Quantity = decimal.Parse(data["quantity"]),
            FilledQuantity = decimal.Parse(data["filledQuantity"]),
            Status = (OrderStatus)int.Parse(data["status"]),
            CreatedAt = long.Parse(data["createdAt"]), // ✅ long 不是 DateTimeOffset
            UpdatedAt = long.Parse(data["updatedAt"])  // ✅ long 不是 DateTimeOffset
        };
    }

    private Trade MapToTrade(Dictionary<string, string> data)
    {
        return new Trade
        {
            Id = int.Parse(data["id"]),
            TradingPairId = int.Parse(data["tradingPairId"]), // ✅ 使用 TradingPairId 不是 Symbol
            BuyOrderId = int.Parse(data["buyOrderId"]),
            SellOrderId = int.Parse(data["sellOrderId"]),
            Price = decimal.Parse(data["price"]),
            Quantity = decimal.Parse(data["quantity"]),
            BuyerId = int.Parse(data["buyerId"]),      // ✅ BuyerId 不是 BuyerUserId
            SellerId = int.Parse(data["sellerId"]),    // ✅ SellerId 不是 SellerUserId
            ExecutedAt = long.Parse(data["executedAt"]) // ✅ ExecutedAt(long) 不是 Timestamp
        };
    }

    private class SyncQueueItem
    {
        public int orderId { get; set; }
        public int? tradeId { get; set; }
        public int userId { get; set; }
        public string symbol { get; set; } = ""; // ✅ symbol 不是 currency
        public string operation { get; set; } = "";
        public long timestamp { get; set; }
    }

    #endregion
}
