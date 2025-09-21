using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Caching;
using CryptoSpot.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 缓存服务实现
    /// </summary>
    public class CacheService : ICacheService, IDisposable
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ICacheEventService _cacheEventService;
        private readonly ILogger<CacheService> _logger;
        private readonly SemaphoreSlim _cacheSemaphore = new(1, 1);

        // 缓存存储
        private readonly ConcurrentDictionary<int, User> _userCache = new();
        private readonly ConcurrentDictionary<string, TradingPair> _tradingPairCache = new();
        private readonly ConcurrentDictionary<int, int> _userIndexCache = new(); // userId -> index
        private readonly ConcurrentDictionary<string, string> _tradingPairIndexCache = new(); // symbol -> index
        private readonly ConcurrentDictionary<int, Dictionary<string, Asset>> _userAssetCache = new(); // userId -> {symbol -> Asset}

        private bool _isInitialized = false;

        public CacheService(
            IServiceScopeFactory serviceScopeFactory,
            ICacheEventService cacheEventService,
            ILogger<CacheService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _cacheEventService = cacheEventService;
            _logger = logger;

            // 订阅缓存更新事件
            _cacheEventService.UserChanged += OnUserChanged;
            _cacheEventService.TradingPairChanged += OnTradingPairChanged;
        }

        public async Task<IEnumerable<User>> GetCachedUsersAsync()
        {
            await EnsureInitializedAsync();
            return _userCache.Values.ToList();
        }

        public async Task<IEnumerable<TradingPair>> GetCachedTradingPairsAsync()
        {
            await EnsureInitializedAsync();
            return _tradingPairCache.Values.ToList();
        }

        public async Task<User?> GetCachedUserByIdAsync(int userId)
        {
            await EnsureInitializedAsync();
            return _userCache.TryGetValue(userId, out var user) ? user : null;
        }

        public async Task<TradingPair?> GetCachedTradingPairBySymbolAsync(string symbol)
        {
            await EnsureInitializedAsync();
            return _tradingPairCache.TryGetValue(symbol, out var tradingPair) ? tradingPair : null;
        }

        public async Task RefreshUsersCacheAsync()
        {
            await _cacheSemaphore.WaitAsync();
            try
            {
                _logger.LogInformation("开始刷新用户缓存");
                
                using var scope = _serviceScopeFactory.CreateScope();
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var users = await userRepository.GetAllAsync();
                
                // 清除现有缓存
                _userCache.Clear();
                _userIndexCache.Clear();
                
                // 重新填充缓存
                foreach (var user in users)
                {
                    _userCache[user.Id] = user;
                }
                
                _logger.LogInformation("用户缓存刷新完成，缓存了 {Count} 个用户", _userCache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新用户缓存失败");
                throw;
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        public async Task RefreshUsersCacheInternalAsync()
        {
            _logger.LogInformation("开始刷新用户缓存");
            
            using var scope = _serviceScopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var users = await userRepository.GetAllAsync();
            
            // 清除现有缓存
            _userCache.Clear();
            _userIndexCache.Clear();
            
            // 重新填充缓存
            foreach (var user in users)
            {
                _userCache[user.Id] = user;
            }
            
            _logger.LogInformation("用户缓存刷新完成，缓存了 {Count} 个用户", _userCache.Count);
        }

        public async Task RefreshTradingPairsCacheAsync()
        {
            await _cacheSemaphore.WaitAsync();
            try
            {
                _logger.LogInformation("开始刷新交易对缓存");
                
                using var scope = _serviceScopeFactory.CreateScope();
                var tradingPairRepository = scope.ServiceProvider.GetRequiredService<ITradingPairRepository>();
                var tradingPairs = await tradingPairRepository.GetAllAsync();
                
                // 清除现有缓存
                _tradingPairCache.Clear();
                _tradingPairIndexCache.Clear();
                
                // 重新填充缓存
                foreach (var tradingPair in tradingPairs)
                {
                    _tradingPairCache[tradingPair.Symbol] = tradingPair;
                }
                
                _logger.LogInformation("交易对缓存刷新完成，缓存了 {Count} 个交易对", _tradingPairCache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新交易对缓存失败");
                throw;
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        public async Task RefreshTradingPairsCacheInternalAsync()
        {
            _logger.LogInformation("开始刷新交易对缓存");
            
            using var scope = _serviceScopeFactory.CreateScope();
            var tradingPairRepository = scope.ServiceProvider.GetRequiredService<ITradingPairRepository>();
            var tradingPairs = await tradingPairRepository.GetAllAsync();
            
            // 清除现有缓存
            _tradingPairCache.Clear();
            _tradingPairIndexCache.Clear();
            
            // 重新填充缓存
            foreach (var tradingPair in tradingPairs)
            {
                _tradingPairCache[tradingPair.Symbol] = tradingPair;
            }
            
            _logger.LogInformation("交易对缓存刷新完成，缓存了 {Count} 个交易对", _tradingPairCache.Count);
        }

        public async Task RefreshAllCacheAsync()
        {
            await _cacheSemaphore.WaitAsync();
            try
            {
                _logger.LogInformation("开始刷新所有缓存");
                
                // 使用内部方法避免死锁（不获取锁）
                await Task.WhenAll(
                    RefreshUsersCacheInternalAsync(),
                    RefreshTradingPairsCacheInternalAsync(),
                    RefreshUserAssetsCacheInternalAsync()
                );
                
                _isInitialized = true;
                _logger.LogInformation("所有缓存刷新完成");
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        public async Task ClearAllCacheAsync()
        {
            await _cacheSemaphore.WaitAsync();
            try
            {
                _userCache.Clear();
                _tradingPairCache.Clear();
                _userIndexCache.Clear();
                _tradingPairIndexCache.Clear();
                _isInitialized = false;
                
                _logger.LogInformation("所有缓存已清除");
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (!_isInitialized)
            {
                await RefreshAllCacheAsync();
            }
        }

        private async Task OnUserChanged(int userId)
        {
            try
            {
                _logger.LogDebug("处理用户变更事件: UserId={UserId}", userId);
                
                using var scope = _serviceScopeFactory.CreateScope();
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                
                // 从数据库重新获取该用户数据
                var user = await userRepository.GetByIdAsync(userId);
                if (user != null)
                {
                    _userCache[userId] = user;
                    _logger.LogDebug("用户缓存已更新: UserId={UserId}", userId);
                }
                else
                {
                    // 用户被删除，从缓存中移除
                    _userCache.TryRemove(userId, out _);
                    _logger.LogDebug("用户已从缓存中移除: UserId={UserId}", userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理用户变更事件失败: UserId={UserId}", userId);
            }
        }

        private async Task OnTradingPairChanged(string symbol)
        {
            try
            {
                _logger.LogDebug("处理交易对变更事件: Symbol={Symbol}", symbol);
                
                using var scope = _serviceScopeFactory.CreateScope();
                var tradingPairRepository = scope.ServiceProvider.GetRequiredService<ITradingPairRepository>();
                
                // 从数据库重新获取该交易对数据
                var tradingPair = await tradingPairRepository.GetBySymbolAsync(symbol);
                if (tradingPair != null)
                {
                    _tradingPairCache[symbol] = tradingPair;
                    _logger.LogDebug("交易对缓存已更新: Symbol={Symbol}", symbol);
                }
                else
                {
                    // 交易对被删除，从缓存中移除
                    _tradingPairCache.TryRemove(symbol, out _);
                    _logger.LogDebug("交易对已从缓存中移除: Symbol={Symbol}", symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理交易对变更事件失败: Symbol={Symbol}", symbol);
            }
        }

        private async Task RefreshUserAssetsCacheInternalAsync()
        {
            _logger.LogInformation("开始刷新用户资产缓存");
            
            using var scope = _serviceScopeFactory.CreateScope();
            var assetRepository = scope.ServiceProvider.GetRequiredService<IRepository<Asset>>();
            var assets = await assetRepository.GetAllAsync();
            
            // 清除现有资产缓存
            _userAssetCache.Clear();
            
            // 按用户分组资产，过滤掉UserId为null的资产
            var assetsByUser = assets
                .Where(a => a.UserId.HasValue)
                .GroupBy(a => a.UserId!.Value)
                .ToList();
            
            foreach (var userAssets in assetsByUser)
            {
                var userId = userAssets.Key;
                var userAssetDict = new Dictionary<string, Asset>();
                
                foreach (var asset in userAssets)
                {
                    userAssetDict[asset.Symbol] = asset;
                }
                
                _userAssetCache[userId] = userAssetDict;
            }
            
            _logger.LogInformation("用户资产缓存刷新完成，缓存了 {UserCount} 个用户的资产", _userAssetCache.Count);
        }

        public async Task<Asset?> GetCachedUserAssetAsync(int userId, string symbol)
        {
            await EnsureInitializedAsync();
            
            if (_userAssetCache.TryGetValue(userId, out var userAssets))
            {
                return userAssets.TryGetValue(symbol, out var asset) ? asset : null;
            }
            
            return null;
        }

        public async Task<Dictionary<string, Asset>> GetCachedUserAssetsAsync(int userId)
        {
            await EnsureInitializedAsync();
            
            if (_userAssetCache.TryGetValue(userId, out var userAssets))
            {
                return new Dictionary<string, Asset>(userAssets);
            }
            
            return new Dictionary<string, Asset>();
        }

        public void Dispose()
        {
            _cacheSemaphore?.Dispose();
        }
    }
}
