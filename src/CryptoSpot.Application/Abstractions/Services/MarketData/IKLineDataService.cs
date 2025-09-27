using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Services.MarketData
{
    /// <summary>
    /// 领域K线数据服务接口（返回领域实体）。
    /// </summary>
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
