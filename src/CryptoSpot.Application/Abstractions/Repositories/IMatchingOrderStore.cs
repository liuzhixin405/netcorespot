// filepath: g:\\github\\netcorespot\\src\\CryptoSpot.Application\\Abstractions\\Repositories\\IMatchingOrderStore.cs
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Repositories
{
    /// <summary>
    /// 撮合/系统任务专用的最小订单存取接口（避免 DTO 开销）。
    /// 禁止在常规应用服务 / 控制器中直接使用。
    /// </summary>
    public interface IMatchingOrderStore
    {
        Task<Order?> GetOrderAsync(int orderId);
        Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null);
        Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, OrderStatus? status = null, int limit = 100);
        Task<Order> AddOrderAsync(Order order);
        Task<bool> UpdateOrderStatusAsync(int orderId, OrderStatus status, decimal filledQuantityDelta = 0, decimal? averagePrice = null);
        Task<bool> CancelOrderAsync(int orderId);
    }
}
