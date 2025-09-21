using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.Trading
{
    /// <summary>
    /// 订单匹配引擎接口
    /// </summary>
    public interface IOrderMatchingEngine
    {
        /// <summary>
        /// 处理订单（匹配和执行）
        /// </summary>
        Task<OrderMatchResult> ProcessOrderAsync(Order order);
        
        /// <summary>
        /// 匹配指定交易对的订单
        /// </summary>
        Task<List<Trade>> MatchOrdersAsync(string symbol);
        
        /// <summary>
        /// 获取订单簿深度
        /// </summary>
        Task<OrderBookDepth> GetOrderBookDepthAsync(string symbol, int depth = 20);
        
        /// <summary>
        /// 取消订单
        /// </summary>
        Task<bool> CancelOrderAsync(int orderId);
        
        /// <summary>
        /// 检查订单是否可以匹配
        /// </summary>
        Task<bool> CanMatchOrderAsync(Order buyOrder, Order sellOrder);
    }

    /// <summary>
    /// 订单匹配结果
    /// </summary>
    public class OrderMatchResult
    {
        public Order Order { get; set; } = null!;
        public List<Trade> Trades { get; set; } = new();
        public bool IsFullyMatched { get; set; }
        public decimal TotalMatchedQuantity { get; set; }
        public decimal AveragePrice { get; set; }
    }

    /// <summary>
    /// 订单簿深度
    /// </summary>
    public class OrderBookDepth
    {
        public string Symbol { get; set; } = string.Empty;
        public List<OrderBookLevel> Bids { get; set; } = new(); // 买单
        public List<OrderBookLevel> Asks { get; set; } = new(); // 卖单
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 订单簿价格层级
    /// </summary>
    public class OrderBookLevel
    {
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public decimal Total { get; set; }
        public int OrderCount { get; set; }
    }
}
