using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.Mapping;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 订单服务基础设施实现（由 Application.Services.RefactoredOrderService 迁移）
    /// </summary>
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

        public async Task<Order> CreateOrderAsync(int userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null)
        {
            try
            {
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol) ?? throw new ArgumentException($"交易对 {symbol} 不存在");
                if (quantity <= 0) throw new ArgumentException("数量必须大于0", nameof(quantity));
                if (type == OrderType.Limit && (!price.HasValue || price.Value <= 0)) throw new ArgumentException("限价单必须提供正价格", nameof(price));

                // 防御性精度统一（向下截断确保不超出可支付金额）
                quantity = RoundDown(quantity, tradingPair.QuantityPrecision);
                if (type == OrderType.Limit && price.HasValue)
                    price = RoundDown(price.Value, tradingPair.PricePrecision);
                if (quantity <= 0 || (type == OrderType.Limit && price.HasValue && price.Value <= 0))
                    throw new ArgumentException("精度归一后数量或价格无效");

                var initialStatus = type == OrderType.Limit ? OrderStatus.Active : OrderStatus.Pending;

                var order = new Order
                {
                    UserId = userId,
                    TradingPairId = tradingPair.Id,
                    OrderId = GenerateOrderId(),
                    Side = side,
                    Type = type,
                    Quantity = quantity,
                    Price = price,
                    Status = initialStatus,
                    ClientOrderId = GenerateOrderId(),
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var createdOrder = await _orderRepository.AddAsync(order);
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("订单创建成功: {OrderId} Status={Status}", order.OrderId, order.Status);
                return createdOrder;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建订单时出错: UserId={UserId}, Symbol={Symbol}", userId, symbol);
                throw;
            }
        }

        public async Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null) => await _orderRepository.GetActiveOrdersAsync(symbol);

        public async Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, OrderStatus? status = null, int limit = 100) => await _orderRepository.GetUserOrdersAsync(userId, null, status, limit);

        public async Task<Order?> GetOrderByIdAsync(int orderId, int? userId = null)
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null) return null;
            if (userId.HasValue && order.UserId != userId.Value) return null;
            return order;
        }

        public Task<Order?> GetOrderByOrderIdStringAsync(string orderIdString) => _orderRepository.GetOrderByOrderIdStringAsync(orderIdString);

        public async Task UpdateOrderStatusAsync(int orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0)
        {
            try
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null) return;

                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (filledQuantity > 0)
                {
                    var previousFilled = order.FilledQuantity;
                    var previousAvg = order.AveragePrice;
                    var newFilled = previousFilled + filledQuantity;

                    if (averagePrice > 0)
                    {
                        order.AveragePrice = previousFilled <= 0 ? averagePrice : (previousAvg * previousFilled + averagePrice * filledQuantity) / newFilled;
                    }

                    order.FilledQuantity = newFilled;
                    if (newFilled >= order.Quantity && order.Quantity > 0) status = OrderStatus.Filled;
                    else if (newFilled > 0 && status != OrderStatus.Cancelled && status != OrderStatus.Rejected) status = OrderStatus.PartiallyFilled;
                }

                order.Status = status;
                order.UpdatedAt = now;
                await _orderRepository.UpdateAsync(order);
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("订单状态更新成功: {OrderId} -> {Status} Filled={Filled}/{Qty} Avg={Avg}", order.OrderId, order.Status, order.FilledQuantity, order.Quantity, order.AveragePrice);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新订单状态时出错: OrderId={OrderId}, Status={Status}", orderId, status);
                throw;
            }
        }

        public async Task<bool> CancelOrderAsync(int orderId, int? userId)
        {
            try
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null || (userId.HasValue && order.UserId != userId.Value)) return false;
                if (order.Status != OrderStatus.Active && order.Status != OrderStatus.Pending) return false;
                await UpdateOrderStatusAsync(orderId, OrderStatus.Cancelled);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订单时出错: OrderId={OrderId}, UserId={UserId}", orderId, userId);
                throw;
            }
        }

        public Task<IEnumerable<Order>> GetOrdersForOrderBookAsync(string symbol, OrderSide side, int depth) => _orderRepository.GetOrdersForOrderBookAsync(symbol, side, depth);

        public Task<IEnumerable<Order>> GetExpiredOrdersAsync(TimeSpan expireAfter)
        {
            var expireTime = DateTimeOffset.UtcNow.Add(-expireAfter).ToUnixTimeMilliseconds();
            return _orderRepository.FindAsync(o => o.CreatedAt < expireTime && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Active));
        }

        // ========== 新增 DTO 方法实现 ==========
        public async Task<ApiResponseDto<OrderDto?>> CreateOrderDtoAsync(int userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null)
        {
            try
            {
                var order = await CreateOrderAsync(userId, symbol, side, type, quantity, price);
                var dto = _mappingService.MapToDto(order);
                return ApiResponseDto<OrderDto?>.CreateSuccess(dto, "订单创建成功");
            }
            catch (ArgumentException aex)
            {
                return ApiResponseDto<OrderDto?>.CreateError(aex.Message, "ORDER_INVALID_ARGUMENT");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建订单(DTO)失败: UserId={UserId}, Symbol={Symbol}", userId, symbol);
                return ApiResponseDto<OrderDto?>.CreateError("订单创建失败", "ORDER_CREATE_ERROR");
            }
        }

        public async Task<ApiResponseDto<bool>> CancelOrderDtoAsync(int orderId, int? userId)
        {
            try
            {
                var success = await CancelOrderAsync(orderId, userId);
                return success ? ApiResponseDto<bool>.CreateSuccess(true, "订单取消成功") : ApiResponseDto<bool>.CreateError("订单取消失败", "ORDER_CANCEL_FAILED");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订单(DTO)失败: OrderId={OrderId}", orderId);
                return ApiResponseDto<bool>.CreateError("订单取消失败", "ORDER_CANCEL_ERROR");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<OrderDto>>> GetUserOrdersDtoAsync(int userId, OrderStatus? status = null, int limit = 100)
        {
            try
            {
                var orders = await GetUserOrdersAsync(userId, status, limit);
                var dto = _mappingService.MapToDto(orders);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户订单(DTO)失败: UserId={UserId}", userId);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateError("获取用户订单失败", "ORDER_QUERY_ERROR");
            }
        }

        public async Task<ApiResponseDto<OrderDto?>> GetOrderByIdDtoAsync(int orderId, int? userId)
        {
            try
            {
                var order = await GetOrderByIdAsync(orderId, userId);
                var dto = order != null ? _mappingService.MapToDto(order) : null;
                if (dto == null)
                    return ApiResponseDto<OrderDto?>.CreateError("订单不存在", "ORDER_NOT_FOUND");
                return ApiResponseDto<OrderDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取订单(DTO)失败: OrderId={OrderId}", orderId);
                return ApiResponseDto<OrderDto?>.CreateError("获取订单失败", "ORDER_QUERY_ERROR");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<OrderDto>>> GetActiveOrdersDtoAsync(string? symbol = null)
        {
            try
            {
                var orders = await GetActiveOrdersAsync(symbol);
                var dto = _mappingService.MapToDto(orders);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃订单(DTO)失败: Symbol={Symbol}", symbol);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateError("获取活跃订单失败", "ORDER_QUERY_ERROR");
            }
        }

        public async Task<ApiResponseDto<bool>> UpdateOrderStatusDtoAsync(int orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0)
        {
            try
            {
                await UpdateOrderStatusAsync(orderId, status, filledQuantity, averagePrice);
                return ApiResponseDto<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新订单状态(DTO)失败: OrderId={OrderId}", orderId);
                return ApiResponseDto<bool>.CreateError("更新订单状态失败", "ORDER_UPDATE_ERROR");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<OrderDto>>> GetExpiredOrdersDtoAsync(TimeSpan expireAfter)
        {
            try
            {
                var orders = await GetExpiredOrdersAsync(expireAfter);
                var dto = _mappingService.MapToDto(orders);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取过期订单(DTO)失败");
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateError("获取过期订单失败", "ORDER_QUERY_ERROR");
            }
        }

        private string GenerateOrderId() => $"ORD_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000, 9999)}";

        private static decimal RoundDown(decimal value, int precision)
        {
            if (precision < 0) precision = 0;
            var factor = (decimal)Math.Pow(10, precision);
            return Math.Truncate(value * factor) / factor;
        }
    }
}