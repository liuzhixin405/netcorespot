using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Trading;
using Microsoft.Extensions.Logging;

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

        public OrderService(
            IOrderRepository orderRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            ILogger<OrderService> logger)
        {
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
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

        private string GenerateOrderId() => $"ORD_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000, 9999)}";

        private static decimal RoundDown(decimal value, int precision)
        {
            if (precision < 0) precision = 0;
            var factor = (decimal)Math.Pow(10, precision);
            return Math.Truncate(value * factor) / factor;
        }
    }
}