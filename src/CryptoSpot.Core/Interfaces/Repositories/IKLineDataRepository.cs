using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Core.Interfaces.Repositories
{
    /// <summary>
    /// K线数据仓储接口
    /// </summary>
    public interface IKLineDataRepository : IRepository<KLineData>
    {
        /// <summary>
        /// 根据交易对ID获取K线数据
        /// </summary>
        /// <param name="tradingPairId">交易对ID</param>
        /// <param name="interval">时间间隔</param>
        /// <param name="limit">限制数量</param>
        /// <returns>K线数据列表</returns>
        Task<IEnumerable<KLineData>> GetKLineDataByTradingPairIdAsync(int tradingPairId, string interval, int limit = 100);

        /// <summary>
        /// 根据时间范围获取K线数据
        /// </summary>
        /// <param name="tradingPairId">交易对ID</param>
        /// <param name="interval">时间间隔</param>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <returns>K线数据列表</returns>
        Task<IEnumerable<KLineData>> GetKLineDataByTimeRangeAsync(int tradingPairId, string interval, DateTime startTime, DateTime endTime);

        /// <summary>
        /// 获取最新的K线数据
        /// </summary>
        /// <param name="tradingPairId">交易对ID</param>
        /// <param name="interval">时间间隔</param>
        /// <returns>最新K线数据</returns>
        Task<KLineData?> GetLatestKLineDataAsync(int tradingPairId, string interval);

        /// <summary>
        /// 批量保存K线数据
        /// </summary>
        /// <param name="klineData">K线数据列表</param>
        /// <returns>保存的数据数量</returns>
        Task<int> SaveKLineDataBatchAsync(IEnumerable<KLineData> klineData);

        /// <summary>
        /// 更新或插入K线数据
        /// </summary>
        /// <param name="klineData">K线数据</param>
        /// <returns>是否成功</returns>
        Task<bool> UpsertKLineDataAsync(KLineData klineData);

        /// <summary>
        /// 删除过期的K线数据
        /// </summary>
        /// <param name="tradingPairId">交易对ID</param>
        /// <param name="interval">时间间隔</param>
        /// <param name="keepDays">保留天数</param>
        /// <returns>删除的数据数量</returns>
        Task<int> DeleteExpiredKLineDataAsync(int tradingPairId, string interval, int keepDays = 30);

        /// <summary>
        /// 获取K线数据统计
        /// </summary>
        /// <param name="tradingPairId">交易对ID</param>
        /// <param name="interval">时间间隔</param>
        /// <returns>K线数据统计</returns>
        Task<KLineDataStatistics> GetKLineDataStatisticsAsync(int tradingPairId, string interval);

        /// <summary>
        /// 获取K线数据
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <param name="interval">时间间隔</param>
        /// <param name="limit">限制数量</param>
        /// <returns>K线数据列表</returns>
        Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100);
    }

    /// <summary>
    /// K线数据统计信息
    /// </summary>
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