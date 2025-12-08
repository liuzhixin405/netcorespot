using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.Mapping;

namespace CryptoSpot.Infrastructure.Services
{
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<OrderService> _logger;
        private readonly IDtoMappingService _mappingService;

        public OrderService(
            IOrderRepository orderRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            ILogger<OrderService> logger,
            IDtoMappingService mappingService)
        {
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _mappingService = mappingService;
        }

        public Task<ApiResponseDto<OrderDto?>> CreateOrderDtoAsync(long userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null)
        {
            return ServiceHelper.ExecuteAsync<OrderDto?>(async () =>
            {
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol) ?? throw new ArgumentException($"交易对 {symbol} 不存在");
                if (quantity <= 0) throw new ArgumentException("数量必须大于0", nameof(quantity));
                if (type == OrderType.Limit && (!price.HasValue || price.Value <= 0)) throw new ArgumentException("限价单必须提供正价格", nameof(price));

                quantity = ServiceHelper.RoundDown(quantity, tradingPair.QuantityPrecision);
                if (type == OrderType.Limit && price.HasValue)
                    price = ServiceHelper.RoundDown(price.Value, tradingPair.PricePrecision);
                if (quantity <= 0 || (type == OrderType.Limit && price.HasValue && price.Value <= 0))
                    throw new ArgumentException("精度归一后数量或价格无效");

                var order = new Order
                {
                    UserId = userId,
                    TradingPairId = tradingPair.Id,
                    OrderId = ServiceHelper.GenerateId("ORD"),
                    Side = side,
                    Type = type,
                    Quantity = quantity,
                    Price = price,
                    Status = OrderStatus.Pending,
                    ClientOrderId = ServiceHelper.GenerateId("ORD"),
                    CreatedAt = ServiceHelper.NowMs(),
                    UpdatedAt = ServiceHelper.NowMs()
                };

                var createdOrder = await _orderRepository.AddAsync(order);
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("Order created: {OrderId} Status={Status}", order.OrderId, order.Status);
                return _mappingService.MapToDto(createdOrder);
            }, _logger, "订单创建失败");
        }

        public Task<ApiResponseDto<bool>> CancelOrderDtoAsync(long orderId, long? userId)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null || (userId.HasValue && order.UserId != userId.Value))
                    throw new InvalidOperationException("订单不存在");
                if (order.Status != OrderStatus.Active && order.Status != OrderStatus.Pending)
                    throw new InvalidOperationException("订单状态不允许取消");

                await UpdateOrderStatusInternalAsync(order, OrderStatus.Cancelled);
                return true;
            }, _logger, "订单取消失败");
        }

        public Task<ApiResponseDto<IEnumerable<OrderDto>>> GetUserOrdersDtoAsync(long userId, OrderStatus? status = null, int limit = 100)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var orders = await _orderRepository.GetUserOrdersAsync(userId, null, status, limit);
                return _mappingService.MapToDto(orders);
            }, _logger, "获取用户订单失败");
        }

        public Task<ApiResponseDto<OrderDto?>> GetOrderByIdDtoAsync(long orderId, long? userId)
        {
            return ServiceHelper.ExecuteAsync<OrderDto?>(async () =>
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null || (userId.HasValue && order.UserId != userId.Value))
                    throw new InvalidOperationException("订单不存在");
                return _mappingService.MapToDto(order);
            }, _logger, "获取订单失败");
        }

        public Task<ApiResponseDto<IEnumerable<OrderDto>>> GetActiveOrdersDtoAsync(string? symbol = null)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var orders = await _orderRepository.GetActiveOrdersAsync(symbol);
                return _mappingService.MapToDto(orders);
            }, _logger, "获取活跃订单失败");
        }

        public Task<ApiResponseDto<bool>> UpdateOrderStatusDtoAsync(long orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var order = await _orderRepository.GetByIdAsync(orderId) ?? throw new InvalidOperationException("订单不存在");
                await UpdateOrderStatusInternalAsync(order, status, filledQuantity, averagePrice);
                return true;
            }, _logger, "更新订单状态失败");
        }

        public Task<ApiResponseDto<IEnumerable<OrderDto>>> GetExpiredOrdersDtoAsync(TimeSpan expireAfter)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var expireTime = ServiceHelper.NowMs() - (long)expireAfter.TotalMilliseconds;
                var orders = await _orderRepository.FindAsync(o => o.CreatedAt < expireTime && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Active));
                return _mappingService.MapToDto(orders);
            }, _logger, "获取过期订单失败");
        }

        private async Task UpdateOrderStatusInternalAsync(Order order, OrderStatus status, decimal filledQuantityDelta = 0, decimal averagePrice = 0)
        {
            var now = ServiceHelper.NowMs();
            if (filledQuantityDelta > 0)
            {
                var previousFilled = order.FilledQuantity;
                var previousAvg = order.AveragePrice;
                var newFilled = previousFilled + filledQuantityDelta;
                if (averagePrice > 0)
                    order.AveragePrice = previousFilled <= 0 ? averagePrice : (previousAvg * previousFilled + averagePrice * filledQuantityDelta) / newFilled;
                order.FilledQuantity = newFilled;
                if (newFilled >= order.Quantity && order.Quantity > 0) status = OrderStatus.Filled;
                else if (newFilled > 0 && status != OrderStatus.Cancelled && status != OrderStatus.Rejected) status = OrderStatus.PartiallyFilled;
            }
            order.Status = status;
            order.UpdatedAt = now;
            await _orderRepository.UpdateAsync(order);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
