using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.Repositories
{
    public interface IKLineDataRepository : IRepository<KLineData>
    {
        Task<IEnumerable<KLineData>> GetBySymbolAndTimeFrameAsync(string symbol, string timeFrame, int limit = 100);
        Task<KLineData?> GetLatestAsync(string symbol, string timeFrame);
        Task AddOrUpdateAsync(KLineData klineData);
        Task<IEnumerable<KLineData>> GetRecentDataAsync(string symbol, string timeFrame, long fromTimestamp);
        Task<IEnumerable<KLineData>> GetRecentDataAsync(string symbol, string timeFrame, DateTime fromTime);
    }
}
