using CryptoSpot.Redis;
using Microsoft.Extensions.Logging;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using StackExchange.Redis;
using System.Text.Json;

namespace CryptoSpot.Infrastructure.Repositories.Redis;

/// <summary>
/// Redis 资产仓储（运行时所有资产操作都在 Redis 中）
/// </summary>
public class RedisAssetRepository
{
    private readonly IRedisCache _redis;
    private readonly IDatabase _db; // 原生 Redis API
    private readonly ILogger<RedisAssetRepository> _logger;

    // 精度：8 位小数 = 100,000,000
    private const long PRECISION = 100_000_000;

    public RedisAssetRepository(IRedisCache redis, ILogger<RedisAssetRepository> logger)
    {
        _redis = redis;
        _db = redis.Connection.GetDatabase(); // 获取底层 Redis 数据库
        _logger = logger;
    }

    #region 资产查询

    /// <summary>
    /// 获取用户资产
    /// </summary>
    public async Task<Asset?> GetAssetAsync(int userId, string symbol)
    {
        var key = $"asset:{userId}:{symbol}";
        var exists = await _redis.ExistsAsync(key);
        if (!exists) return null;

        var data = await _redis.HGetAllAsync(key);
        return MapToAsset(data);
    }

    /// <summary>
    /// 获取用户所有资产
    /// </summary>
    public async Task<List<Asset>> GetUserAssetsAsync(int userId)
    {
        var symbolsArray = await _db.SetMembersAsync($"user_assets:{userId}");
        var assets = new List<Asset>();

        foreach (var symbol in symbolsArray)
        {
            var asset = await GetAssetAsync(userId, symbol.ToString());
            if (asset != null)
            {
                assets.Add(asset);
            }
        }

        return assets;
    }

    #endregion

    #region 资产冻结/解冻（使用 Lua 保证原子性）

    /// <summary>
    /// 冻结资产（可用 → 冻结）
    /// </summary>
    public async Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount)
    {
        var key = $"asset:{userId}:{symbol}";
        var amountLong = (long)(amount * PRECISION);

        // Lua 脚本确保原子性
        var script = @"
            local available = tonumber(redis.call('HGET', KEYS[1], 'availableBalance'))
            if available >= tonumber(ARGV[1]) then
                redis.call('HINCRBY', KEYS[1], 'availableBalance', -ARGV[1])
                redis.call('HINCRBY', KEYS[1], 'frozenBalance', ARGV[1])
                redis.call('HSET', KEYS[1], 'updatedAt', ARGV[2])
                return 1
            else
                return 0
            end
        ";

        var result = await _db.ScriptEvaluateAsync(script,  
            new RedisKey[] { key }, 
            new RedisValue[] { amountLong, DateTimeExtensions.GetCurrentUnixTimeMilliseconds() });

        var success = result.ToString() == "1";

        if (success)
        {
            _logger.LogDebug("🔒 冻结资产: UserId={UserId} {Symbol} Amount={Amount}", 
                userId, symbol, amount);
            await EnqueueAssetSync(userId, symbol);
        }
        else
        {
            _logger.LogWarning("⚠️ 冻结资产失败（余额不足）: UserId={UserId} {Symbol} Amount={Amount}",
                userId, symbol, amount);
        }

        return success;
    }

    /// <summary>
    /// 解冻资产（冻结 → 可用）
    /// </summary>
    public async Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount)
    {
        var key = $"asset:{userId}:{symbol}";
        var amountLong = (long)(amount * PRECISION);

        var script = @"
            local frozen = tonumber(redis.call('HGET', KEYS[1], 'frozenBalance'))
            if frozen >= tonumber(ARGV[1]) then
                redis.call('HINCRBY', KEYS[1], 'frozenBalance', -ARGV[1])
                redis.call('HINCRBY', KEYS[1], 'availableBalance', ARGV[1])
                redis.call('HSET', KEYS[1], 'updatedAt', ARGV[2])
                return 1
            else
                return 0
            end
        ";

        var result = await _db.ScriptEvaluateAsync(script,
            new RedisKey[] { key },
            new RedisValue[] { amountLong, DateTimeExtensions.GetCurrentUnixTimeMilliseconds() });

        var success = result.ToString() == "1";

        if (success)
        {
            _logger.LogDebug("🔓 解冻资产: UserId={UserId} {Symbol} Amount={Amount}",
                userId, symbol, amount);
            await EnqueueAssetSync(userId, symbol);
        }

        return success;
    }

    /// <summary>
    /// 扣除冻结资产（用于成交后扣款）
    /// </summary>
    public async Task<bool> DeductFrozenAssetAsync(int userId, string symbol, decimal amount)
    {
        var key = $"asset:{userId}:{symbol}";
        var amountLong = (long)(amount * PRECISION);

        var script = @"
            local frozen = tonumber(redis.call('HGET', KEYS[1], 'frozenBalance'))
            if frozen >= tonumber(ARGV[1]) then
                redis.call('HINCRBY', KEYS[1], 'frozenBalance', -ARGV[1])
                redis.call('HSET', KEYS[1], 'updatedAt', ARGV[2])
                return 1
            else
                return 0
            end
        ";

        var result = await _db.ScriptEvaluateAsync(script,
            new RedisKey[] { key },
            new RedisValue[] { amountLong, DateTimeExtensions.GetCurrentUnixTimeMilliseconds() });

        var success = result.ToString() == "1";

        if (success)
        {
            _logger.LogDebug("💸 扣除冻结资产: UserId={UserId} {Symbol} Amount={Amount}",
                userId, symbol, amount);
            await EnqueueAssetSync(userId, symbol);
        }

        return success;
    }

    /// <summary>
    /// 增加可用资产（用于成交后入账）
    /// </summary>
    public async Task<bool> AddAvailableAssetAsync(int userId, string symbol, decimal amount)
    {
        var key = $"asset:{userId}:{symbol}";
        var amountLong = (long)(amount * PRECISION);

        var script = LuaScript.Prepare(@"
            redis.call('HINCRBY', KEYS[1], 'availableBalance', ARGV[1])
            redis.call('HSET', KEYS[1], 'updatedAt', ARGV[2])
            return 1
        ");

        var result = await _db.ScriptEvaluateAsync(script,
            new { key = (RedisKey)key, amountLong, updatedAt = DateTimeExtensions.GetCurrentUnixTimeMilliseconds() });

        _logger.LogDebug("💰 增加可用资产: UserId={UserId} {Symbol} Amount={Amount}",
            userId, symbol, amount);

        await EnqueueAssetSync(userId, symbol);
        return true;
    }

    #endregion

    #region 资产创建和保存

    /// <summary>
    /// 创建或更新资产
    /// </summary>
    public async Task SaveAssetAsync(Asset asset)
    {
        var key = $"asset:{asset.UserId}:{asset.Symbol}";

        var hashEntries = new Dictionary<string, string>
        {
            ["userId"] = asset.UserId?.ToString() ?? "",
            ["symbol"] = asset.Symbol,
            ["availableBalance"] = ((long)(asset.Available * PRECISION)).ToString(),
            ["frozenBalance"] = ((long)(asset.Frozen * PRECISION)).ToString(),
            ["createdAt"] = asset.CreatedAt.ToString(),
            ["updatedAt"] = asset.UpdatedAt.ToString()
        };

        await _redis.HMSetAsync(key, hashEntries.ToArray());

        // 添加到用户资产索引
        await _db.SetAddAsync($"user_assets:{asset.UserId}", asset.Symbol);

        _logger.LogDebug("💾 保存资产: UserId={UserId} {Symbol} Available={Available} Frozen={Frozen}",
            asset.UserId, asset.Symbol, asset.Available, asset.Frozen);
    }

    #endregion

    #region 批量操作

    /// <summary>
    /// 批量获取资产（用于数据加载）
    /// </summary>
    public async Task SaveAssetsAsync(List<Asset> assets)
    {
        foreach (var asset in assets)
        {
            await SaveAssetAsync(asset);
        }

        _logger.LogInformation("📦 批量保存资产: {Count} 条", assets.Count);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 映射 Redis Hash 为 Asset 对象
    /// </summary>
    private Asset MapToAsset(Dictionary<string, string> data)
    {
        if (data == null || data.Count == 0) return null!;

        var available = long.Parse(data["availableBalance"]);
        var frozen = long.Parse(data["frozenBalance"]);

        return new Asset
        {
            UserId = string.IsNullOrEmpty(data["userId"]) ? null : int.Parse(data["userId"]),
            Symbol = data["symbol"],
            Available = (decimal)available / PRECISION,
            Frozen = (decimal)frozen / PRECISION,
            CreatedAt = long.Parse(data["createdAt"]),
            UpdatedAt = long.Parse(data["updatedAt"])
        };
    }

    /// <summary>
    /// 将资产变更加入同步队列
    /// </summary>
    private async Task EnqueueAssetSync(int userId, string symbol)
    {
        var syncData = new
        {
            userId,
            symbol,
            timestamp = DateTimeExtensions.GetCurrentUnixTimeMilliseconds()
        };

        var json = JsonSerializer.Serialize(syncData);
        await _db.ListRightPushAsync("sync_queue:assets", json);
    }

    #endregion
}
