using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Repositories
{
    public interface IOrderRepository : IRepository<Order>
    {
        Task<IEnumerable<Order>> GetOrdersByUserIdAsync(long userId, string? symbol = null, OrderStatus? status = null);
        Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null);
        Task<IEnumerable<Order>> GetActiveOrdersByTradingPairIdAsync(long tradingPairId);
        Task<bool> UpdateOrderStatusAsync(long orderId, OrderStatus status, decimal? filledQuantity = null, decimal? averagePrice = null);
        Task<IEnumerable<Order>> GetUserOrderHistoryAsync(long userId, string? symbol = null, int limit = 100);
        Task<IEnumerable<Order>> GetUserOrdersAsync(long userId, string? symbol = null, OrderStatus? status = null, int limit = 100);
        Task<Order?> GetOrderByOrderIdStringAsync(string orderIdString);
        Task<IEnumerable<Order>> GetOrdersForOrderBookAsync(string symbol, OrderSide side, int depth);
    }
}
