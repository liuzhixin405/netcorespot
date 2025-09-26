using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Core.Interfaces.Trading
{
    /// <summary>
    /// 订单服务接口
    /// </summary>
    public interface IOrderService
    {
        /// <summary>
        /// 创建订单
        /// </summary>
        Task<Order> CreateOrderAsync(int userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null);
        
        /// <summary>
        /// 取消订单
        /// </summary>
        Task<bool> CancelOrderAsync(int orderId, int? userId);
        
        /// <summary>
        /// 获取用户订单
        /// </summary>
        Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, OrderStatus? status = null, int limit = 100);
        
        /// <summary>
        /// 获取订单详情
        /// </summary>
        Task<Order?> GetOrderByIdAsync(int orderId, int? userId);
        
        /// <summary>
        /// 获取活跃订单
        /// </summary>
        Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null);
        
        /// <summary>
        /// 更新订单状态
        /// </summary>
        Task UpdateOrderStatusAsync(int orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0);
        
        /// <summary>
        /// 获取过期订单
        /// </summary>
        Task<IEnumerable<Order>> GetExpiredOrdersAsync(TimeSpan expireAfter);
    }
}
