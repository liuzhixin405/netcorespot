using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Repositories
{
    public interface IKLineDataRepository : IRepository<KLineData>
    {
        Task<IEnumerable<KLineData>> GetKLineDataByTradingPairIdAsync(long tradingPairId, string interval, int limit = 100);
        Task<IEnumerable<KLineData>> GetKLineDataByTimeRangeAsync(long tradingPairId, string interval, DateTime startTime, DateTime endTime);
        Task<KLineData?> GetLatestKLineDataAsync(long tradingPairId, string interval);
        Task<int> SaveKLineDataBatchAsync(IEnumerable<KLineData> klineData);
        Task<bool> UpsertKLineDataAsync(KLineData klineData);
        Task<int> DeleteExpiredKLineDataAsync(long tradingPairId, string interval, int keepDays = 30);
        Task<KLineDataStatistics> GetKLineDataStatisticsAsync(long tradingPairId, string interval);
        Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100);
    }

    public class KLineDataStatistics
    {
        public int TotalRecords { get; set; }
        public DateTime? FirstRecordTime { get; set; }
        public DateTime? LastRecordTime { get; set; }
        public decimal HighestPrice { get; set; }
        public decimal LowestPrice { get; set; }
        public decimal TotalVolume { get; set; }
    }
}
