using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Trading;

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    public interface IOrderService
    {
        // ========== DTO 接口 (已统一) ==========
        Task<ApiResponseDto<OrderDto?>> CreateOrderDtoAsync(long userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null);
        Task<ApiResponseDto<bool>> CancelOrderDtoAsync(long orderId, long? userId);
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetUserOrdersDtoAsync(long userId, OrderStatus? status = null, int limit = 100);
        Task<ApiResponseDto<OrderDto?>> GetOrderByIdDtoAsync(long orderId, long? userId);
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetActiveOrdersDtoAsync(string? symbol = null);
        Task<ApiResponseDto<bool>> UpdateOrderStatusDtoAsync(long orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0);
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetExpiredOrdersDtoAsync(TimeSpan expireAfter);
    }
}
