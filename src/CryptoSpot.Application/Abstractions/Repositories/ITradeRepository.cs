using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Repositories
{
    public interface ITradeRepository : IRepository<Trade>
    {
        Task<IEnumerable<Trade>> GetTradesByUserIdAsync(int userId, string? symbol = null, int limit = 100);
        Task<IEnumerable<Trade>> GetTradesByTradingPairIdAsync(int tradingPairId, int limit = 100);
        Task<IEnumerable<Trade>> GetRecentTradesAsync(string? symbol = null, int limit = 50);
        Task<IEnumerable<Trade>> GetTradesByTimeRangeAsync(DateTime startTime, DateTime endTime, string? symbol = null);
        Task<TradeStatistics> GetTradeStatisticsAsync(int? userId = null, string? symbol = null, DateTime? startTime = null, DateTime? endTime = null);
        Task<IEnumerable<Trade>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100);
    }

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
