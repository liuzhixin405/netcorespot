using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Trading;

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    public interface IOrderService
    {
        // ========== DTO 接口 (已统一) ==========
        Task<ApiResponseDto<OrderDto?>> CreateOrderDtoAsync(int userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null);
        Task<ApiResponseDto<bool>> CancelOrderDtoAsync(int orderId, int? userId);
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetUserOrdersDtoAsync(int userId, OrderStatus? status = null, int limit = 100);
        Task<ApiResponseDto<OrderDto?>> GetOrderByIdDtoAsync(int orderId, int? userId);
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetActiveOrdersDtoAsync(string? symbol = null);
        Task<ApiResponseDto<bool>> UpdateOrderStatusDtoAsync(int orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0);
        Task<ApiResponseDto<IEnumerable<OrderDto>>> GetExpiredOrdersDtoAsync(TimeSpan expireAfter);
    }
}
