using CryptoSpot.Domain.Entities;
using CryptoSpot.Core.Extensions;

namespace CryptoSpot.Core.Interfaces.Trading
{
    /// <summary>
    /// 自动交易服务接口
    /// </summary>
    public interface IAutoTradingService
    {
        /// <summary>
        /// 启动自动交易
        /// </summary>
        Task StartAutoTradingAsync();
        
        /// <summary>
        /// 停止自动交易
        /// </summary>
        Task StopAutoTradingAsync();
        
        /// <summary>
        /// 为指定交易对创建做市订单
        /// </summary>
        Task CreateMarketMakingOrdersAsync(string symbol);
        
        /// <summary>
        /// 取消过期的系统订单
        /// </summary>
        Task CancelExpiredSystemOrdersAsync();
        
        /// <summary>
        /// 重新平衡系统资产
        /// </summary>
        Task RebalanceSystemAssetsAsync();
        
        /// <summary>
        /// 获取系统交易统计
        /// </summary>
        Task<AutoTradingStats> GetTradingStatsAsync(int systemAccountId);
    }

    /// <summary>
    /// 自动交易统计
    /// </summary>
    public class AutoTradingStats
    {
        public int UserId { get; set; }
        public decimal DailyVolume { get; set; }
        public decimal DailyProfit { get; set; }
        public int ActiveOrdersCount { get; set; }
        public int TotalTradesCount { get; set; }
        public Dictionary<string, decimal> AssetBalances { get; set; } = new();
        public long LastUpdated { get; set; } = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();
    }
}
