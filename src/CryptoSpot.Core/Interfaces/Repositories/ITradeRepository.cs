using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.Repositories
{
    /// <summary>
    /// 交易记录仓储接口
    /// </summary>
    public interface ITradeRepository : IRepository<Trade>
    {
        /// <summary>
        /// 根据用户ID获取交易记录
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <param name="limit">限制数量</param>
        /// <returns>交易记录列表</returns>
        Task<IEnumerable<Trade>> GetTradesByUserIdAsync(int userId, string? symbol = null, int limit = 100);

        /// <summary>
        /// 根据交易对ID获取交易记录
        /// </summary>
        /// <param name="tradingPairId">交易对ID</param>
        /// <param name="limit">限制数量</param>
        /// <returns>交易记录列表</returns>
        Task<IEnumerable<Trade>> GetTradesByTradingPairIdAsync(int tradingPairId, int limit = 100);

        /// <summary>
        /// 获取最近的交易记录
        /// </summary>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <param name="limit">限制数量</param>
        /// <returns>最近的交易记录</returns>
        Task<IEnumerable<Trade>> GetRecentTradesAsync(string? symbol = null, int limit = 50);

        /// <summary>
        /// 根据时间范围获取交易记录
        /// </summary>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <returns>交易记录列表</returns>
        Task<IEnumerable<Trade>> GetTradesByTimeRangeAsync(DateTime startTime, DateTime endTime, string? symbol = null);

        /// <summary>
        /// 获取交易统计信息
        /// </summary>
        /// <param name="userId">用户ID（可选）</param>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <param name="startTime">开始时间（可选）</param>
        /// <param name="endTime">结束时间（可选）</param>
        /// <returns>交易统计</returns>
        Task<TradeStatistics> GetTradeStatisticsAsync(int? userId = null, string? symbol = null, DateTime? startTime = null, DateTime? endTime = null);

        /// <summary>
        /// 获取交易历史
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <param name="limit">限制数量</param>
        /// <returns>交易历史列表</returns>
        Task<IEnumerable<Trade>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100);
    }

    /// <summary>
    /// 交易统计信息
    /// </summary>
    public class TradeStatistics
    {
        public int TotalTrades { get; set; }
        public decimal TotalVolume { get; set; }
        public decimal TotalValue { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal HighestPrice { get; set; }
        public decimal LowestPrice { get; set; }
    }
}
