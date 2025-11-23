using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Persistence.Redis.Repositories;

namespace CryptoSpot.Infrastructure.Services
{
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<OrderService> _logger;
        private readonly IDtoMappingService _mappingService;
    private readonly RedisOrderRepository? _redisOrderRepository;

        public OrderService(
            IOrderRepository orderRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            ILogger<OrderService> logger,
            IDtoMappingService mappingService,
            RedisOrderRepository? redisOrderRepository = null)
        {
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _mappingService = mappingService;
            _redisOrderRepository = redisOrderRepository;
        }

        // ========== DTO 方法实现 ==========
        public async Task<ApiResponseDto<OrderDto?>> CreateOrderDtoAsync(long userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null)
        {
            try
            {
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol) ?? throw new ArgumentException($"交易对 {symbol} 不存在");
                if (quantity <= 0) throw new ArgumentException("数量必须大于0", nameof(quantity));
                if (type == OrderType.Limit && (!price.HasValue || price.Value <= 0)) throw new ArgumentException("限价单必须提供正价格", nameof(price));

                quantity = RoundDown(quantity, tradingPair.QuantityPrecision);
                if (type == OrderType.Limit && price.HasValue)
                    price = RoundDown(price.Value, tradingPair.PricePrecision);
                if (quantity <= 0 || (type == OrderType.Limit && price.HasValue && price.Value <= 0))
                    throw new ArgumentException("精度归一后数量或价格无效");

                var initialStatus = OrderStatus.Pending; // 所有新订单都从Pending开始，等待撮合引擎处理

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

                // Prefer Redis repository (cache-first) when available
                // If Redis order repo is available, prefer creating via Redis (cache-first)
                if (_redisOrderRepository != null)
                {
                    try
                    {
                        var tp = await _tradingPairRepository.GetBySymbolAsync(symbol);
                        var sym = tp?.Symbol ?? symbol;
                        var createdRedis = await _redisOrderRepository.CreateOrderAsync(order, sym);
                        _logger.LogInformation("[Redis] Order placed via RedisOrderRepository: {OrderId} Status={Status}", createdRedis.Id, createdRedis.Status);
                        var dtoRedis = _mappingService.MapToDto(createdRedis);
                        return ApiResponseDto<OrderDto?>.CreateSuccess(dtoRedis, "订单创建成功");
                    }
                    catch (Exception rex)
                    {
                        _logger.LogWarning(rex, "Redis order create failed, falling back to DB");
                    }
                }

                var createdOrderDb = await _orderRepository.AddAsync(order);
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("Order created (DB): {OrderId} Status={Status}", order.OrderId, order.Status);
                var dtoDb = _mappingService.MapToDto(createdOrderDb);
                // Best-effort backfill to Redis if repository available
                try
                {
                    if (_redisOrderRepository != null)
                    {
                        var tp = await _tradingPairRepository.GetBySymbolAsync(symbol);
                        var sym = tp?.Symbol ?? symbol;
                        await _redisOrderRepository.SeedOrderAsync(createdOrderDb, sym);
                        _logger.LogInformation("[Backfill] Seeded DB-created order to Redis: {OrderId}", createdOrderDb.Id);
                    }
                }
                catch (Exception rex)
                {
                    _logger.LogWarning(rex, "Failed to seed DB-created order to Redis: OrderId={OrderId}", createdOrderDb.Id);
                }
                return ApiResponseDto<OrderDto?>.CreateSuccess(dtoDb, "订单创建成功");
            }
            catch (ArgumentException aex)
            {
                return ApiResponseDto<OrderDto?>.CreateError(aex.Message, "ORDER_INVALID_ARGUMENT");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建订单失败: UserId={UserId}, Symbol={Symbol}", userId, symbol);
                return ApiResponseDto<OrderDto?>.CreateError("订单创建失败", "ORDER_CREATE_ERROR");
            }
        }

        public async Task<ApiResponseDto<bool>> CancelOrderDtoAsync(long orderId, long? userId)
        {
            try
            {
                // Prefer Redis cancellation when available
                if (_redisOrderRepository != null)
                {
                    var order = await _redisOrderRepository.GetOrderByIdAsync(orderId);
                    if (order == null || (userId.HasValue && order.UserId != userId.Value))
                        return ApiResponseDto<bool>.CreateError("订单不存在", "ORDER_NOT_FOUND");
                    if (order.Status != OrderStatus.Active && order.Status != OrderStatus.Pending)
                        return ApiResponseDto<bool>.CreateError("订单状态不允许取消", "ORDER_CANCEL_INVALID_STATE");

                    var success = await _redisOrderRepository.CancelOrderAsync(orderId, userId ?? 0);
                    if (!success) return ApiResponseDto<bool>.CreateError("订单无法取消", "ORDER_CANCEL_FAILED");
                    return ApiResponseDto<bool>.CreateSuccess(true, "订单取消成功");
                }

                // Fallback to DB
                var orderDb = await _orderRepository.GetByIdAsync(orderId);
                if (orderDb == null || (userId.HasValue && orderDb.UserId != userId.Value))
                    return ApiResponseDto<bool>.CreateError("订单不存在", "ORDER_NOT_FOUND");
                if (orderDb.Status != OrderStatus.Active && orderDb.Status != OrderStatus.Pending)
                    return ApiResponseDto<bool>.CreateError("订单状态不允许取消", "ORDER_CANCEL_INVALID_STATE");

                await UpdateOrderStatusInternalAsync(orderDb, OrderStatus.Cancelled);
                return ApiResponseDto<bool>.CreateSuccess(true, "订单取消成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订单失败: OrderId={OrderId}", orderId);
                return ApiResponseDto<bool>.CreateError("订单取消失败", "ORDER_CANCEL_ERROR");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<OrderDto>>> GetUserOrdersDtoAsync(long userId, OrderStatus? status = null, int limit = 100)
        {
            try
            {
                // Prefer Redis (cache-first) when available
                try
                {
                    if (_redisOrderRepository != null)
                    {
                        var redisOrders = await _redisOrderRepository.GetUserOrdersAsync(userId, limit);
                        if (redisOrders != null && redisOrders.Any())
                        {
                            var dtoRedis = _mappingService.MapToDto(redisOrders);
                            return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dtoRedis);
                        }
                    }
                }
                catch (Exception rex)
                {
                    _logger.LogDebug(rex, "Redis GetUserOrdersAsync failed, falling back to DB for UserId={UserId}", userId);
                }

                var orders = await _orderRepository.GetUserOrdersAsync(userId, null, status, limit);
                var dto = _mappingService.MapToDto(orders);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户订单失败: UserId={UserId}", userId);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateError("获取用户订单失败", "ORDER_QUERY_ERROR");
            }
        }

        public async Task<ApiResponseDto<OrderDto?>> GetOrderByIdDtoAsync(long orderId, long? userId)
        {
            try
            {
                // Prefer Redis (cache-first) when available
                try
                {
                    if (_redisOrderRepository != null)
                    {
                        var redisOrder = await _redisOrderRepository.GetOrderByIdAsync(orderId);
                        if (redisOrder != null && (!userId.HasValue || redisOrder.UserId == userId.Value))
                        {
                            var dtoRedis = _mappingService.MapToDto(redisOrder);
                            return ApiResponseDto<OrderDto?>.CreateSuccess(dtoRedis);
                        }
                    }
                }
                catch (Exception rex)
                {
                    _logger.LogDebug(rex, "Redis lookup failed in GetOrderByIdDtoAsync, falling back to DB for OrderId={OrderId}", orderId);
                }

                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null || (userId.HasValue && order.UserId != userId.Value))
                    return ApiResponseDto<OrderDto?>.CreateError("订单不存在", "ORDER_NOT_FOUND");
                var dto = _mappingService.MapToDto(order);
                return ApiResponseDto<OrderDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取订单失败: OrderId={OrderId}", orderId);
                return ApiResponseDto<OrderDto?>.CreateError("获取订单失败", "ORDER_QUERY_ERROR");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<OrderDto>>> GetActiveOrdersDtoAsync(string? symbol = null)
        {
            try
            {
                // Prefer Redis (cache-first) when available
                try
                {
                    if (_redisOrderRepository != null)
                    {
                        var buyOrders = await _redisOrderRepository.GetActiveOrdersAsync(symbol ?? string.Empty, OrderSide.Buy);
                        var sellOrders = await _redisOrderRepository.GetActiveOrdersAsync(symbol ?? string.Empty, OrderSide.Sell);
                        var combined = new List<Order>();
                        if (buyOrders != null) combined.AddRange(buyOrders);
                        if (sellOrders != null) combined.AddRange(sellOrders);
                        if (combined.Any())
                        {
                            var dtoRedis = _mappingService.MapToDto(combined);
                            return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dtoRedis);
                        }
                    }
                }
                catch (Exception rex)
                {
                    _logger.LogDebug(rex, "Redis GetActiveOrdersAsync failed, falling back to DB for Symbol={Symbol}", symbol);
                }

                var orders = await _orderRepository.GetActiveOrdersAsync(symbol);
                var dto = _mappingService.MapToDto(orders);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃订单失败: Symbol={Symbol}", symbol);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateError("获取活跃订单失败", "ORDER_QUERY_ERROR");
            }
        }

        public async Task<ApiResponseDto<bool>> UpdateOrderStatusDtoAsync(long orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0)
        {
            try
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null)
                    return ApiResponseDto<bool>.CreateError("订单不存在", "ORDER_NOT_FOUND");

                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (filledQuantity > 0)
                {
                    var previousFilled = order.FilledQuantity;
                    var previousAvg = order.AveragePrice;
                    var newFilled = previousFilled + filledQuantity;
                    if (averagePrice > 0)
                        order.AveragePrice = previousFilled <= 0 ? averagePrice : (previousAvg * previousFilled + averagePrice * filledQuantity) / newFilled;
                    order.FilledQuantity = newFilled;
                    if (newFilled >= order.Quantity && order.Quantity > 0) status = OrderStatus.Filled;
                    else if (newFilled > 0 && status != OrderStatus.Cancelled && status != OrderStatus.Rejected) status = OrderStatus.PartiallyFilled;
                }
                order.Status = status;
                order.UpdatedAt = now;
                await _orderRepository.UpdateAsync(order);
                await _unitOfWork.SaveChangesAsync();
                return ApiResponseDto<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新订单状态失败: OrderId={OrderId}", orderId);
                return ApiResponseDto<bool>.CreateError("更新订单状态失败", "ORDER_UPDATE_ERROR");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<OrderDto>>> GetExpiredOrdersDtoAsync(TimeSpan expireAfter)
        {
            try
            {
                var expireTime = DateTimeOffset.UtcNow.Add(-expireAfter).ToUnixTimeMilliseconds();
                var orders = await _orderRepository.FindAsync(o => o.CreatedAt < expireTime && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Active));
                var dto = _mappingService.MapToDto(orders);
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取过期订单失败");
                return ApiResponseDto<IEnumerable<OrderDto>>.CreateError("获取过期订单失败", "ORDER_QUERY_ERROR");
            }
        }

        private async Task UpdateOrderStatusInternalAsync(Order order, OrderStatus status, decimal filledQuantityDelta = 0, decimal averagePrice = 0)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
            // Prefer Redis update when available
            if (_redisOrderRepository != null)
            {
                try
                {
                    await _redisOrderRepository.UpdateOrderStatusAsync(order.Id, status, order.FilledQuantity);
                    return;
                }
                catch (Exception rex)
                {
                    _logger.LogWarning(rex, "Redis update order status failed, falling back to DB: OrderId={OrderId}", order.Id);
                }
            }

            await _orderRepository.UpdateAsync(order);
            await _unitOfWork.SaveChangesAsync();
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