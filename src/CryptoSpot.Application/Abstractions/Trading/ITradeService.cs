using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Trading
{
    public interface ITradeService
    {
        Task<Trade> ExecuteTradeAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity);
        Task<IEnumerable<Trade>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100);
        Task<IEnumerable<Trade>> GetUserTradesAsync(int userId, string symbol = "", int limit = 100);
        Task<IEnumerable<Trade>> GetRecentTradesAsync(string symbol, int limit = 50);
        Task<Trade?> GetTradeByIdAsync(long tradeId);
        Task<IEnumerable<Trade>> GetTradesByOrderIdAsync(int orderId);
        Task<IEnumerable<Trade>> GetOrderTradesAsync(int orderId) => GetTradesByOrderIdAsync(orderId);
        Task<decimal> GetTradingVolumeAsync(string symbol, TimeSpan timeRange);
        Task<(decimal high, decimal low)> GetPriceRangeAsync(string symbol, TimeSpan timeRange);
    }
}
