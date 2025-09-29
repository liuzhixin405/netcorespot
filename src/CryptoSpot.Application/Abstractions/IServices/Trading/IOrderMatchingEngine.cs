using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Services.RealTime;

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    public interface IOrderMatchingEngine
    {
        Task<OrderMatchResult> ProcessOrderAsync(Order order);
        Task<List<Trade>> MatchOrdersAsync(string symbol);
        Task<OrderBookDepth> GetOrderBookDepthAsync(string symbol, int depth = 20);
        Task<bool> CancelOrderAsync(int orderId);
        Task<bool> CanMatchOrderAsync(Order buyOrder, Order sellOrder);
    }

    public class OrderMatchResult
    {
        public Order Order { get; set; } = null!;
        public List<Trade> Trades { get; set; } = new();
        public bool IsFullyMatched { get; set; }
        public decimal TotalMatchedQuantity { get; set; }
        public decimal AveragePrice { get; set; }
    }

    public class OrderBookDepth
    {
        public string Symbol { get; set; } = string.Empty;
        public List<OrderBookLevel> Bids { get; set; } = new();
        public List<OrderBookLevel> Asks { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class OrderBookLevel
    {
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public decimal Total { get; set; }
        public int OrderCount { get; set; }
    }
}
