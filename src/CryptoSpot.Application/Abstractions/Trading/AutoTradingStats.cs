using CryptoSpot.Domain.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoSpot.Application.Abstractions.Trading
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
