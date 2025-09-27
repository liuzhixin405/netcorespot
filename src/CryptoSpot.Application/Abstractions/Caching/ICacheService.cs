using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Caching
{
    public interface ICacheService
    {
        Task<IEnumerable<User>> GetCachedUsersAsync();
        Task<IEnumerable<TradingPair>> GetCachedTradingPairsAsync();
        Task<User?> GetCachedUserByIdAsync(int userId);
        Task<TradingPair?> GetCachedTradingPairBySymbolAsync(string symbol);
        Task RefreshUsersCacheAsync();
        Task RefreshTradingPairsCacheAsync();
        Task RefreshAllCacheAsync();
        Task ClearAllCacheAsync();
        Task<Asset?> GetCachedUserAssetAsync(int userId, string symbol);
        Task<Dictionary<string, Asset>> GetCachedUserAssetsAsync(int userId);
        Task InvalidateUserOrdersCacheAsync(int userId);
        Task InvalidateUserAssetsCacheAsync(int userId);
        Task InvalidateTradingPairCacheAsync(string symbol);
        Task InvalidateUserTradesCacheAsync(int userId);
        Task UpdateTradingPairPriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);
        Task UpdateKLineDataCacheAsync(KLineData klineData);
    }
}
