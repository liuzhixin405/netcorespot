using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Core.Interfaces.Caching
{
    /// <summary>
    /// 缓存服务接口
    /// </summary>
    public interface ICacheService
    {
        /// <summary>
        /// 获取缓存的用户数据
        /// </summary>
        Task<IEnumerable<User>> GetCachedUsersAsync();
        
        /// <summary>
        /// 获取缓存的交易对数据
        /// </summary>
        Task<IEnumerable<TradingPair>> GetCachedTradingPairsAsync();
        
        /// <summary>
        /// 根据ID获取缓存的用户
        /// </summary>
        Task<User?> GetCachedUserByIdAsync(int userId);
        
        /// <summary>
        /// 根据符号获取缓存的交易对
        /// </summary>
        Task<TradingPair?> GetCachedTradingPairBySymbolAsync(string symbol);
        
        /// <summary>
        /// 刷新用户缓存
        /// </summary>
        Task RefreshUsersCacheAsync();
        
        /// <summary>
        /// 刷新交易对缓存
        /// </summary>
        Task RefreshTradingPairsCacheAsync();
        
        /// <summary>
        /// 刷新所有缓存
        /// </summary>
        Task RefreshAllCacheAsync();
        
        /// <summary>
        /// 清除所有缓存
        /// </summary>
        Task ClearAllCacheAsync();
        
        /// <summary>
        /// 获取缓存的用户资产
        /// </summary>
        Task<Asset?> GetCachedUserAssetAsync(int userId, string symbol);
        
        /// <summary>
        /// 获取用户的所有缓存资产
        /// </summary>
        Task<Dictionary<string, Asset>> GetCachedUserAssetsAsync(int userId);
        
        /// <summary>
        /// 使用户订单缓存失效
        /// </summary>
        Task InvalidateUserOrdersCacheAsync(int userId);
        
        /// <summary>
        /// 使用户资产缓存失效
        /// </summary>
        Task InvalidateUserAssetsCacheAsync(int userId);
        
        /// <summary>
        /// 使交易对缓存失效
        /// </summary>
        Task InvalidateTradingPairCacheAsync(string symbol);
        
        /// <summary>
        /// 使用户交易缓存失效
        /// </summary>
        Task InvalidateUserTradesCacheAsync(int userId);
        
        /// <summary>
        /// 更新交易对价格缓存
        /// </summary>
        Task UpdateTradingPairPriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);
        
        /// <summary>
        /// 更新K线数据缓存
        /// </summary>
        Task UpdateKLineDataCacheAsync(KLineData klineData);
    }
}
