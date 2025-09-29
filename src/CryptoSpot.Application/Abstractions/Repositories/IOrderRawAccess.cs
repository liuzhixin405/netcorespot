// filepath: g:\\github\\netcorespot\\src\\CryptoSpot.Application\\Abstractions\\Repositories\\IOrderRawAccess.cs
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Repositories
{
    /// <summary>
    /// 提供撮合/系统任务所需的最小化订单原始访问接口，避免服务层 DTO 开销。
    /// 仅限内部 (撮合引擎、自动交易、清算任务) 使用，不对外暴露。
    /// </summary>
    public interface IOrderRawAccess
    {
        Task<Order?> GetOrderRawAsync(int orderId);
        Task<IEnumerable<Order>> GetActiveOrdersRawAsync(string? symbol = null);
        Task<IEnumerable<Order>> GetUserOrdersRawAsync(int userId, OrderStatus? status = null, int limit = 100);
        Task<Order> AddOrderRawAsync(Order order);
        Task<bool> UpdateOrderStatusRawAsync(int orderId, OrderStatus status, decimal filledQuantityDelta = 0, decimal? averagePrice = null);
        Task<bool> CancelOrderRawAsync(int orderId);
    }
}
