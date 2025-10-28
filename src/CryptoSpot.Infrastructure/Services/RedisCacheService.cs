using CryptoSpot.Domain.Entities;
using CryptoSpot.Redis;
using CryptoSpot.Application.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using StackExchange.Redis;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 基于Redis的缓存服务
    /// </summary>
    public class RedisCacheService : IDisposable, IKLineCache
    {
        private readonly IRedisCache _redisCache;
        private readonly ILogger<RedisCacheService> _logger;
        
        // 缓存键前缀
        private const string USER_CACHE_PREFIX = "cache:user:";
        private const string TRADING_PAIR_CACHE_PREFIX = "cache:trading_pair:";
        private const string USER_ASSET_CACHE_PREFIX = "cache:user_asset:";
        private const string PRICE_CACHE_PREFIX = "cache:price:";
        private const string KLINE_CACHE_PREFIX = "cache:kline:";
        
        // 缓存过期时间
        private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(30);
        private readonly TimeSpan _priceExpiration = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _klineExpiration = TimeSpan.FromHours(1);

        public RedisCacheService(IRedisCache redisCache, ILogger<RedisCacheService> logger)
        {
            _redisCache = redisCache;
            _logger = logger;
        }

        #region User Cache Methods

        public async Task<User?> GetUserAsync(int userId)
        {
            try
            {
                var key = $"{USER_CACHE_PREFIX}{userId}";
                var userJson = await _redisCache.GetAsync<string>(key);
                
                if (string.IsNullOrEmpty(userJson))
                    return null;

                return JsonSerializer.Deserialize<User>(userJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user from cache: UserId={UserId}", userId);
                return null;
            }
        }

        public async Task SetUserAsync(User user, TimeSpan? expiration = null)
        {
            try
            {
                var key = $"{USER_CACHE_PREFIX}{user.Id}";
                var userJson = JsonSerializer.Serialize(user);
                
                if (expiration.HasValue)
                {
                    await _redisCache.AddAsync(key, userJson, expiration.Value);
                }
                else
                {
                    await _redisCache.AddAsync(key, userJson, _defaultExpiration);
                }
                
                _logger.LogDebug("User cached: UserId={UserId}", user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cache user: UserId={UserId}", user.Id);
            }
        }

        public async Task RemoveUserAsync(int userId)
        {
            try
            {
                var key = $"{USER_CACHE_PREFIX}{userId}";
                await _redisCache.RemoveAsync(key);
                
                _logger.LogDebug("User cache removed: UserId={UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove user from cache: UserId={UserId}", userId);
            }
        }

        #endregion

        #region Trading Pair Cache Methods

        public async Task<TradingPair?> GetTradingPairAsync(string symbol)
        {
            try
            {
                var key = $"{TRADING_PAIR_CACHE_PREFIX}{symbol}";
                var tradingPairJson = await _redisCache.GetAsync<string>(key);
                
                if (string.IsNullOrEmpty(tradingPairJson))
                    return null;

                return JsonSerializer.Deserialize<TradingPair>(tradingPairJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get trading pair from cache: Symbol={Symbol}", symbol);
                return null;
            }
        }

        public async Task SetTradingPairAsync(TradingPair tradingPair, TimeSpan? expiration = null)
        {
            try
            {
                var key = $"{TRADING_PAIR_CACHE_PREFIX}{tradingPair.Symbol}";
                var tradingPairJson = JsonSerializer.Serialize(tradingPair);
                
                if (expiration.HasValue)
                {
                    await _redisCache.AddAsync(key, tradingPairJson, expiration.Value);
                }
                else
                {
                    await _redisCache.AddAsync(key, tradingPairJson, _defaultExpiration);
                }
                
                _logger.LogDebug("Trading pair cached: Symbol={Symbol}", tradingPair.Symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cache trading pair: Symbol={Symbol}", tradingPair.Symbol);
            }
        }

        public async Task RemoveTradingPairAsync(string symbol)
        {
            try
            {
                var key = $"{TRADING_PAIR_CACHE_PREFIX}{symbol}";
                await _redisCache.RemoveAsync(key);
                
                _logger.LogDebug("Trading pair cache removed: Symbol={Symbol}", symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove trading pair from cache: Symbol={Symbol}", symbol);
            }
        }

        public async Task<List<TradingPair>> GetAllTradingPairsAsync()
        {
            try
            {
                var tradingPairs = new List<TradingPair>();
                
                // 使用Redis SCAN命令模式匹配键
                if (_redisCache.Connection != null)
                {
                    var database = _redisCache.Connection.GetDatabase();
                    var pattern = $"{TRADING_PAIR_CACHE_PREFIX}*";
                    
                    foreach (var key in database.Multiplexer.GetServer(database.Multiplexer.GetEndPoints().First()).Keys(pattern: pattern))
                    {
                        var tradingPairJson = await _redisCache.GetAsync<string>(key);
                        if (!string.IsNullOrEmpty(tradingPairJson))
                        {
                            var tradingPair = JsonSerializer.Deserialize<TradingPair>(tradingPairJson);
                            if (tradingPair != null)
                            {
                                tradingPairs.Add(tradingPair);
                            }
                        }
                    }
                }

                return tradingPairs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all trading pairs from cache");
                return new List<TradingPair>();
            }
        }

        #endregion

        #region User Asset Cache Methods

        public async Task<Dictionary<string, Asset>?> GetUserAssetsAsync(int userId)
        {
            try
            {
                var key = $"{USER_ASSET_CACHE_PREFIX}{userId}";
                var assetsJson = await _redisCache.GetAsync<string>(key);
                
                if (string.IsNullOrEmpty(assetsJson))
                    return null;

                return JsonSerializer.Deserialize<Dictionary<string, Asset>>(assetsJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user assets from cache: UserId={UserId}", userId);
                return null;
            }
        }

        public async Task<Asset?> GetUserAssetAsync(int userId, string symbol)
        {
            try
            {
                var userAssets = await GetUserAssetsAsync(userId);
                if (userAssets != null && userAssets.TryGetValue(symbol, out var asset))
                {
                    return asset;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user asset from cache: UserId={UserId}, Symbol={Symbol}", userId, symbol);
                return null;
            }
        }

        public async Task SetUserAssetsAsync(int userId, Dictionary<string, Asset> assets, TimeSpan? expiration = null)
        {
            try
            {
                var key = $"{USER_ASSET_CACHE_PREFIX}{userId}";
                var assetsJson = JsonSerializer.Serialize(assets);
                
                if (expiration.HasValue)
                {
                    await _redisCache.AddAsync(key, assetsJson, expiration.Value);
                }
                else
                {
                    await _redisCache.AddAsync(key, assetsJson, _defaultExpiration);
                }
                
                _logger.LogDebug("User assets cached: UserId={UserId}, AssetCount={Count}", userId, assets.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cache user assets: UserId={UserId}", userId);
            }
        }

        public async Task RemoveUserAssetsAsync(int userId)
        {
            try
            {
                var key = $"{USER_ASSET_CACHE_PREFIX}{userId}";
                await _redisCache.RemoveAsync(key);
                
                _logger.LogDebug("User assets cache removed: UserId={UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove user assets from cache: UserId={UserId}", userId);
            }
        }

        // 标记用户为脏数据，供后台定期落库使用
        private const string DIRTY_USERS_SET = "dirty:users";

        public async Task MarkUserDirtyAsync(int userId)
        {
            try
            {
                // 将 userId 添加到集合中，集合保证唯一性
                await _redisCache.AddAsync($"{USER_CACHE_PREFIX}dirty:{userId}", true, TimeSpan.FromDays(1));
                await _redisCache.AddAsync(DIRTY_USERS_SET, 0); // ensure set existence for implementations that expect it
                if (_redisCache.Connection != null)
                {
                    var db = _redisCache.Connection.GetDatabase();
                    // use a Redis Set to store dirty ids
                    await db.SetAddAsync(DIRTY_USERS_SET, userId);
                }
                else
                {
                    // Fallback: store a list-like key
                    await _redisCache.ListLeftPushAsync("dirty:users:list", userId.ToString());
                }
                _logger.LogDebug("Marked user dirty: UserId={UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark user dirty: UserId={UserId}", userId);
            }
        }

        #endregion

        #region Price Cache Methods

        public async Task<decimal?> GetPriceAsync(string symbol)
        {
            try
            {
                var key = $"{PRICE_CACHE_PREFIX}{symbol}";
                return await _redisCache.GetAsync<decimal?>(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get price from cache: Symbol={Symbol}", symbol);
                return null;
            }
        }

        public async Task SetPriceAsync(string symbol, decimal price)
        {
            try
            {
                var key = $"{PRICE_CACHE_PREFIX}{symbol}";
                await _redisCache.AddAsync(key, price, _priceExpiration);
                
                _logger.LogDebug("Price cached: Symbol={Symbol}, Price={Price}", symbol, price);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cache price: Symbol={Symbol}, Price={Price}", symbol, price);
            }
        }

        // 标记 price 为脏，用于后台批量落库
        private const string DIRTY_PRICES_SET = "dirty:prices";

        public async Task MarkPriceDirtyAsync(string symbol)
        {
            try
            {
                if (_redisCache.Connection != null)
                {
                    var db = _redisCache.Connection.GetDatabase();
                    await db.SetAddAsync(DIRTY_PRICES_SET, symbol);
                }
                else
                {
                    await _redisCache.ListLeftPushAsync("dirty:prices:list", symbol);
                }
                _logger.LogDebug("Marked price dirty: {Symbol}", symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark price dirty: {Symbol}", symbol);
            }
        }

        #endregion

        #region KLine Cache Methods

        public async Task<List<KLineData>?> GetKLineDataAsync(string symbol, string timeFrame, int limit = 100)
        {
            try
            {
                var key = $"{KLINE_CACHE_PREFIX}{symbol}:{timeFrame}";
                var klineJson = await _redisCache.GetAsync<string>(key);
                
                if (string.IsNullOrEmpty(klineJson))
                    return null;

                var allKlines = JsonSerializer.Deserialize<List<KLineData>>(klineJson);
                return allKlines?.TakeLast(limit).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get KLine data from cache: Symbol={Symbol}, TimeFrame={TimeFrame}", symbol, timeFrame);
                return null;
            }
        }

        public async Task SetKLineDataAsync(string symbol, string timeFrame, List<KLineData> klineData)
        {
            try
            {
                var key = $"{KLINE_CACHE_PREFIX}{symbol}:{timeFrame}";
                var klineJson = JsonSerializer.Serialize(klineData);
                await _redisCache.AddAsync(key, klineJson, _klineExpiration);
                
                _logger.LogDebug("KLine data cached: Symbol={Symbol}, TimeFrame={TimeFrame}, Count={Count}", symbol, timeFrame, klineData.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cache KLine data: Symbol={Symbol}, TimeFrame={TimeFrame}", symbol, timeFrame);
            }
        }

        public async Task UpdateKLineDataAsync(string symbol, string timeFrame, KLineData newKline)
        {
            try
            {
                var existingData = await GetKLineDataAsync(symbol, timeFrame, 1000) ?? new List<KLineData>();
                
                // 查找是否已存在相同时间的K线数据
                var existingIndex = existingData.FindIndex(k => k.OpenTime == newKline.OpenTime);
                
                if (existingIndex >= 0)
                {
                    // 更新现有数据
                    existingData[existingIndex] = newKline;
                }
                else
                {
                    // 添加新数据并保持排序
                    existingData.Add(newKline);
                    existingData = existingData.OrderBy(k => k.OpenTime).ToList();
                    
                    // 保持最多1000条记录
                    if (existingData.Count > 1000)
                    {
                        existingData = existingData.TakeLast(1000).ToList();
                    }
                }
                
                await SetKLineDataAsync(symbol, timeFrame, existingData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update KLine data cache: Symbol={Symbol}, TimeFrame={TimeFrame}", symbol, timeFrame);
            }
        }

        // 标记 KLine 为脏，用于后台批量落库
        private const string DIRTY_KLINE_SET = "dirty:kline";

        public async Task MarkKLineDirtyAsync(string symbol, string timeFrame)
        {
            try
            {
                var key = $"{KLINE_CACHE_PREFIX}{symbol}:{timeFrame}";
                // 添加到集合中，元素为 symbol:timeFrame
                if (_redisCache.Connection != null)
                {
                    var db = _redisCache.Connection.GetDatabase();
                    await db.SetAddAsync(DIRTY_KLINE_SET, $"{symbol}:{timeFrame}");
                }
                else
                {
                    await _redisCache.ListLeftPushAsync("dirty:kline:list", $"{symbol}:{timeFrame}");
                }
                _logger.LogDebug("Marked kline dirty: {Symbol}:{TimeFrame}", symbol, timeFrame);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark kline dirty: {Symbol}:{TimeFrame}", symbol, timeFrame);
            }
        }

        #endregion

        #region Utility Methods

        public async Task ClearCacheByPatternAsync(string pattern)
        {
            try
            {
                if (_redisCache.Connection != null)
                {
                    var database = _redisCache.Connection.GetDatabase();
                    var server = database.Multiplexer.GetServer(database.Multiplexer.GetEndPoints().First());
                    var keys = server.Keys(pattern: pattern).ToArray();
                    
                    if (keys.Length > 0)
                    {
                        await database.KeyDeleteAsync(keys);
                        _logger.LogDebug("Cleared cache with pattern: {Pattern}, Keys count: {Count}", pattern, keys.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear cache by pattern: {Pattern}", pattern);
            }
        }

        public async Task ClearAllCacheAsync()
        {
            try
            {
                await Task.WhenAll(
                    ClearCacheByPatternAsync($"{USER_CACHE_PREFIX}*"),
                    ClearCacheByPatternAsync($"{TRADING_PAIR_CACHE_PREFIX}*"),
                    ClearCacheByPatternAsync($"{USER_ASSET_CACHE_PREFIX}*"),
                    ClearCacheByPatternAsync($"{PRICE_CACHE_PREFIX}*"),
                    ClearCacheByPatternAsync($"{KLINE_CACHE_PREFIX}*")
                );
                
                _logger.LogInformation("All cache cleared");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear all cache");
            }
        }

        #endregion

        #region Generic Cache Methods

        /// <summary>
        /// 通用缓存获取方法
        /// </summary>
        public async Task<T?> GetAsync<T>(string key)
        {
            try
            {
                return await _redisCache.GetAsync<T>(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get cache: Key={Key}", key);
                return default(T);
            }
        }

        /// <summary>
        /// 通用缓存设置方法
        /// </summary>
        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan expiration)
        {
            try
            {
                return await _redisCache.AddAsync(key, value, expiration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set cache: Key={Key}", key);
                return false;
            }
        }

        /// <summary>
        /// 通用缓存删除方法
        /// </summary>
        public async Task<bool> RemoveAsync(string key)
        {
            try
            {
                return await _redisCache.RemoveAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove cache: Key={Key}", key);
                return false;
            }
        }

        #endregion

        public void Dispose()
        {
            // Redis connection is managed by DI container, no need to dispose here
        }
    }
}
