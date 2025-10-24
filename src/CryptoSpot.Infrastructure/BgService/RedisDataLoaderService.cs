using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using CryptoSpot.Persistence.Data;
using CryptoSpot.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CryptoSpot.Infrastructure.BgService;

/// <summary>
/// 启动时从 MySQL 加载数据到 Redis（一次性）
/// </summary>
public class RedisDataLoaderService : IHostedService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IRedisCache _redis;
    private readonly IDatabase _db; // 原生 Redis API
    private readonly ILogger<RedisDataLoaderService> _logger;
    private const long PRECISION = 100_000_000; // 8 位小数精度

    public RedisDataLoaderService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IRedisCache redis,
        ILogger<RedisDataLoaderService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _redis = redis;
        _db = redis.Connection.GetDatabase();
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 开始从 MySQL 加载数据到 Redis...");

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // 1. 加载用户数据
            await LoadUsersAsync(dbContext);

            // 2. 加载资产数据
            await LoadAssetsAsync(dbContext);

            // 3. 加载交易对数据
            await LoadTradingPairsAsync(dbContext);

            // 4. 加载活跃订单
            await LoadActiveOrdersAsync(dbContext);

            // 5. 初始化 ID 计数器
            await InitializeIdCountersAsync(dbContext);

            _logger.LogInformation("✅ 数据加载完成！Redis-First 架构已就绪");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 数据加载失败");
            throw; // 加载失败应该停止应用启动
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("⏹️ RedisDataLoaderService 停止");
        return Task.CompletedTask;
    }

    #region 加载用户

    private async Task LoadUsersAsync(ApplicationDbContext db)
    {
        var users = await db.Users.ToListAsync();
        var count = 0;

        foreach (var user in users)
        {
            var key = $"user:{user.Id}";
            var hashEntries = new List<HashEntry>
            {
                new HashEntry("id", user.Id.ToString()),
                new HashEntry("username", user.Username),
                new HashEntry("passwordHash", user.PasswordHash ?? ""),
                new HashEntry("email", user.Email ?? ""),
                new HashEntry("createdAt", user.CreatedAt.ToString())
            };
            
            await _redis.HMSetAsync(key, hashEntries.ToArray());
            count++;
        }

        _logger.LogInformation($"✅ 加载 {count} 个用户");
    }

    #endregion

    #region 加载资产

    private async Task LoadAssetsAsync(ApplicationDbContext db)
    {
        var assets = await db.Assets.ToListAsync();
        var count = 0;

        foreach (var asset in assets)
        {
            var key = $"asset:{asset.UserId}:{asset.Symbol}";
            var hashEntries = new List<HashEntry>
            {
                new HashEntry("userId", asset.UserId?.ToString() ?? ""),
                new HashEntry("symbol", asset.Symbol),
                new HashEntry("availableBalance", ((long)(asset.Available * PRECISION)).ToString()),
                new HashEntry("frozenBalance", ((long)(asset.Frozen * PRECISION)).ToString()),
                new HashEntry("createdAt", asset.CreatedAt.ToString()),
                new HashEntry("updatedAt", asset.UpdatedAt.ToString())
            };
            
            await _redis.HMSetAsync(key, hashEntries.ToArray());

            // 添加到用户资产索引
            await _db.SetAddAsync($"user_assets:{asset.UserId}", asset.Symbol);
            count++;
        }

        _logger.LogInformation($"✅ 加载 {count} 条资产记录");
    }

    #endregion

    #region 加载交易对

    private async Task LoadTradingPairsAsync(ApplicationDbContext db)
    {
        var pairs = await db.TradingPairs.ToListAsync();
        var count = 0;

        foreach (var pair in pairs)
        {
            var key = $"trading_pair:{pair.Symbol}";
            var hashEntries = new List<HashEntry>
            {
                new HashEntry("id", pair.Id.ToString()),
                new HashEntry("symbol", pair.Symbol),
                new HashEntry("baseAsset", pair.BaseAsset),
                new HashEntry("quoteAsset", pair.QuoteAsset),
                new HashEntry("price", pair.Price.ToString()),
                new HashEntry("change24h", pair.Change24h.ToString()),
                new HashEntry("volume24h", pair.Volume24h.ToString()),
                new HashEntry("high24h", pair.High24h.ToString()),
                new HashEntry("low24h", pair.Low24h.ToString()),
                new HashEntry("lastUpdated", pair.LastUpdated.ToString())
            };
            
            await _redis.HMSetAsync(key, hashEntries.ToArray());

            // 添加到交易对列表
            await _db.SetAddAsync("trading_pairs:all", pair.Symbol);
            count++;
        }

        _logger.LogInformation($"✅ 加载 {count} 个交易对");
    }

    #endregion

    #region 加载活跃订单

    private async Task LoadActiveOrdersAsync(ApplicationDbContext db)
    {
        // 只加载活跃、待处理、部分成交的订单
        var activeOrders = await db.Orders
            .Include(o => o.TradingPair)
            .Where(o => o.Status == OrderStatus.Active || 
                       o.Status == OrderStatus.Pending || 
                       o.Status == OrderStatus.PartiallyFilled)
            .ToListAsync();

        var count = 0;

        foreach (var order in activeOrders)
        {
            // 跳过没有交易对的订单
            if (order.TradingPair == null)
            {
                _logger.LogWarning("⚠️ 跳过订单 {OrderId}: TradingPair 为空", order.Id);
                continue;
            }

            var key = $"order:{order.Id}";
            var symbol = order.TradingPair.Symbol;

            var hashEntries = new List<HashEntry>
            {
                new HashEntry("id", order.Id.ToString()),
                new HashEntry("userId", order.UserId?.ToString() ?? ""),
                new HashEntry("tradingPairId", order.TradingPairId.ToString()),
                new HashEntry("symbol", symbol),
                new HashEntry("side", ((int)order.Side).ToString()),
                new HashEntry("type", ((int)order.Type).ToString()),
                new HashEntry("price", order.Price?.ToString() ?? "0"),
                new HashEntry("quantity", order.Quantity.ToString()),
                new HashEntry("filledQuantity", order.FilledQuantity.ToString()),
                new HashEntry("status", ((int)order.Status).ToString()),
                new HashEntry("createdAt", order.CreatedAt.ToString()),
                new HashEntry("updatedAt", order.UpdatedAt.ToString())
            };

            await _redis.HMSetAsync(key, hashEntries.ToArray());

            // 添加到用户订单索引
            await _db.SetAddAsync($"user_orders:{order.UserId}", order.Id.ToString());

            // 添加到活跃订单簿（Sorted Set，按价格排序）
            if (order.Price.HasValue)
            {
                var orderBookKey = $"orders:active:{symbol}:{order.Side}";
                await _db.SortedSetAddAsync(orderBookKey, order.Id.ToString(), (double)order.Price.Value);
            }

            count++;
        }

        _logger.LogInformation($"✅ 加载 {count} 条活跃订单");
    }

    #endregion

    #region 初始化 ID 计数器

    private async Task InitializeIdCountersAsync(ApplicationDbContext db)
    {
        // 订单 ID 计数器
        var maxOrderId = await db.Orders.MaxAsync(o => (int?)o.Id) ?? 0;
        await _db.StringSetAsync("global:order_id", maxOrderId.ToString());

        // 成交 ID 计数器
        var maxTradeId = await db.Set<Trade>().MaxAsync(t => (int?)t.Id) ?? 0;
        await _db.StringSetAsync("global:trade_id", maxTradeId.ToString());

        _logger.LogInformation($"✅ 初始化 ID 计数器: OrderId={maxOrderId}, TradeId={maxTradeId}");
    }

    #endregion
}
