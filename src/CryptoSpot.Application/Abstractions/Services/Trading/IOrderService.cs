using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Trading;

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    public interface IOrderService
    {
        // ========== 旧 Domain 返回接口 (待淘汰) ==========
        [Obsolete("Use DTO methods (ApiResponseDto<OrderDto/...>) instead")] Task<Order> CreateOrderAsync(int userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null);
        [Obsolete] Task<bool> CancelOrderAsync(int orderId, int? userId);
        [Obsolete] Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, OrderStatus? status = null, int limit = 100);
        [Obsolete] Task<Order?> GetOrderByIdAsync(int orderId, int? userId);
        [Obsolete] Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null);
        [Obsolete] Task UpdateOrderStatusAsync(int orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0);
        [Obsolete] Task<IEnumerable<Order>> GetExpiredOrdersAsync(TimeSpan expireAfter);

        // ========== 新 DTO 接口 ==========
        Task<ApiResponseDto<OrderDto?>> CreateOrderDtoAsync(int userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null);
        Task<ApiResponseDto<bool>> CancelOrderDtoAsync(int orderId, int? userId);
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetUserOrdersDtoAsync(int userId, OrderStatus? status = null, int limit = 100);
        Task<ApiResponseDto<OrderDto?>> GetOrderByIdDtoAsync(int orderId, int? userId);
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetActiveOrdersDtoAsync(string? symbol = null);
        Task<ApiResponseDto<bool>> UpdateOrderStatusDtoAsync(int orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0);
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetExpiredOrdersDtoAsync(TimeSpan expireAfter);
    }
}
