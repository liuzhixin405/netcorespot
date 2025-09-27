using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Trading
{
    public interface IOrderService
    {
        Task<Order> CreateOrderAsync(int userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null);
        Task<bool> CancelOrderAsync(int orderId, int? userId);
        Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, OrderStatus? status = null, int limit = 100);
        Task<Order?> GetOrderByIdAsync(int orderId, int? userId);
        Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null);
        Task UpdateOrderStatusAsync(int orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0);
        Task<IEnumerable<Order>> GetExpiredOrdersAsync(TimeSpan expireAfter);
    }
}
