using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Services.MarketData
{
    public interface IMarketDataProvider
    {
        string ProviderName { get; }
        Task<bool> IsAvailableAsync();
        Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100);
        Task<TradingPair?> GetTradingPairAsync(string symbol);
        Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int count = 10);
        Task StartRealTimeDataSyncAsync();
        Task StopRealTimeDataSyncAsync();
    }
}
