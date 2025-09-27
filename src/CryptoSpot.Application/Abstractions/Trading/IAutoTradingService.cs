using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Trading
{
    public interface IAutoTradingService
    {
        Task StartAutoTradingAsync();
        Task StopAutoTradingAsync();
        Task CreateMarketMakingOrdersAsync(string symbol);
        Task CancelExpiredSystemOrdersAsync();
        Task RebalanceSystemAssetsAsync();
        Task<AutoTradingStats> GetTradingStatsAsync(int systemAccountId);
    }
}
