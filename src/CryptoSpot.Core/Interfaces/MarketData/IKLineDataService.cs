using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.MarketData
{
    /// <summary>
    /// K线数据服务接口
    /// </summary>
    public interface IKLineDataService
    {
        /// <summary>
        /// 获取K线数据
        /// </summary>
        Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100);
        
        /// <summary>
        /// 获取历史K线数据
        /// </summary>
        Task<IEnumerable<KLineData>> GetHistoricalKLineDataAsync(string symbol, string interval, DateTime startTime, DateTime endTime);
        
        /// <summary>
        /// 获取最新K线数据
        /// </summary>
        Task<KLineData?> GetLatestKLineDataAsync(string symbol, string interval);
        
        /// <summary>
        /// 添加或更新K线数据
        /// </summary>
        Task<KLineData> AddOrUpdateKLineDataAsync(KLineData klineData);
        
        /// <summary>
        /// 批量添加或更新K线数据
        /// </summary>
        Task<IEnumerable<KLineData>> BatchAddOrUpdateKLineDataAsync(IEnumerable<KLineData> klineDataList);
    }
}
