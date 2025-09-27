using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions; // migrated from CryptoSpot.Core.Extensions

namespace CryptoSpot.Application.Abstractions.Trading // migrated from CryptoSpot.Core.Interfaces.Trading
{
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
