using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Trading;

namespace CryptoSpot.Application.Abstractions.Services.MarketData
{
    public interface IMarketDataProvider
    {
        string ProviderName { get; }
        Task<bool> IsAvailableAsync();
        // 返回 DTO 而不是领域实体
        Task<IEnumerable<KLineDataDto>> GetKLineDataAsync(string symbol, string interval, int limit = 100);
        Task<TradingPairDto?> GetTradingPairAsync(string symbol);
        Task<IEnumerable<TradingPairDto>> GetTopTradingPairsAsync(int count = 10);
        Task StartRealTimeDataSyncAsync();
        Task StopRealTimeDataSyncAsync();
    }
}
