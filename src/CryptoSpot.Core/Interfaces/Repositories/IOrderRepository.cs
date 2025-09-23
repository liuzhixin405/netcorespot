using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.Repositories
{
    /// <summary>
    /// 订单仓储接口
    /// </summary>
    public interface IOrderRepository : IRepository<Order>
    {
        /// <summary>
        /// 根据用户ID获取订单
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <param name="status">订单状态（可选）</param>
        /// <returns>订单列表</returns>
        Task<IEnumerable<Order>> GetOrdersByUserIdAsync(int userId, string? symbol = null, OrderStatus? status = null);

        /// <summary>
        /// 获取活跃订单（待成交或部分成交）
        /// </summary>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <returns>活跃订单列表</returns>
        Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null);

        /// <summary>
        /// 根据交易对ID获取活跃订单
        /// </summary>
        /// <param name="tradingPairId">交易对ID</param>
        /// <returns>活跃订单列表</returns>
        Task<IEnumerable<Order>> GetActiveOrdersByTradingPairIdAsync(int tradingPairId);

        /// <summary>
        /// 获取订单簿深度数据
        /// </summary>
        /// <param name="tradingPairId">交易对ID</param>
        /// <param name="depth">深度</param>
        /// <returns>订单簿数据</returns>
        Task<OrderBookDepth> GetOrderBookDepthAsync(int tradingPairId, int depth = 20);

        /// <summary>
        /// 更新订单状态
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <param name="status">新状态</param>
        /// <param name="filledQuantity">已成交数量（可选）</param>
        /// <param name="averagePrice">平均价格（可选）</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdateOrderStatusAsync(int orderId, OrderStatus status, decimal? filledQuantity = null, decimal? averagePrice = null);

        /// <summary>
        /// 获取用户的历史订单
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <param name="limit">限制数量</param>
        /// <returns>历史订单列表</returns>
        Task<IEnumerable<Order>> GetUserOrderHistoryAsync(int userId, string? symbol = null, int limit = 100);

        /// <summary>
        /// 获取用户订单
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <param name="status">订单状态（可选）</param>
        /// <param name="limit">限制数量</param>
        /// <returns>用户订单列表</returns>
        Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, string? symbol = null, OrderStatus? status = null, int limit = 100);

        /// <summary>
        /// 根据订单ID字符串获取订单
        /// </summary>
        /// <param name="orderIdString">订单ID字符串</param>
        /// <returns>订单对象</returns>
        Task<Order?> GetOrderByOrderIdStringAsync(string orderIdString);

        /// <summary>
        /// 获取订单簿订单
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <param name="side">订单方向</param>
        /// <param name="depth">深度</param>
        /// <returns>订单列表</returns>
        Task<IEnumerable<Order>> GetOrdersForOrderBookAsync(string symbol, OrderSide side, int depth);
    }

    /// <summary>
    /// 订单簿深度数据
    /// </summary>
    public class OrderBookDepth
    {
        public List<OrderBookLevel> Bids { get; set; } = new();
        public List<OrderBookLevel> Asks { get; set; } = new();
    }

    /// <summary>
    /// 订单簿价格级别
    /// </summary>
    public class OrderBookLevel
    {
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public decimal Total { get; set; }
    }
}
