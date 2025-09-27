using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.MarketData
{
    public interface IKLineDataService
    {
        Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100);
        Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, long? startTime, long? endTime, int limit = 100);
        Task<IEnumerable<KLineData>> GetHistoricalKLineDataAsync(string symbol, string interval, long startTime, long endTime);
        Task<KLineData?> GetLatestKLineDataAsync(string symbol, string interval);
        Task<KLineData> AddOrUpdateKLineDataAsync(KLineData klineData);
        Task<IEnumerable<KLineData>> BatchAddOrUpdateKLineDataAsync(IEnumerable<KLineData> klineDataList);
        Task SaveKLineDataAsync(KLineData klineData);
    }
}
