using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Interfaces.Trading;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 重构后的订单服务 - 使用新的仓储模式
    /// </summary>
    public class RefactoredOrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<RefactoredOrderService> _logger;

        public RefactoredOrderService(
            IOrderRepository orderRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            ILogger<RefactoredOrderService> logger)
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
                // 获取交易对信息
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (tradingPair == null)
                {
                    throw new ArgumentException($"交易对 {symbol} 不存在");
                }

                if (quantity <= 0)
                    throw new ArgumentException("数量必须大于0", nameof(quantity));

                if (type == OrderType.Limit && (!price.HasValue || price.Value <= 0))
                    throw new ArgumentException("限价单必须提供正价格", nameof(price));

                // 限价单直接进入撮合队列 -> Active, 市价单先 Pending 等待撮合结果
                var initialStatus = type == OrderType.Limit ? OrderStatus.Active : OrderStatus.Pending;

                // 创建订单
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

                // 精简：无需显式事务，单表写入直接 SaveChanges
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

        public async Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null)
        {
            return await _orderRepository.GetActiveOrdersAsync(symbol);
        }

        public async Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, OrderStatus? status = null, int limit = 100)
        {
            return await _orderRepository.GetUserOrdersAsync(userId, null, status, limit);
        }

        public async Task<Order?> GetOrderByIdAsync(int orderId, int? userId = null)
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            
            if (order == null)
                return null;
                
            if (userId.HasValue && order.UserId != userId.Value)
                return null;
                
            return order;
        }

        public async Task<Order?> GetOrderByOrderIdStringAsync(string orderIdString)
        {
            return await _orderRepository.GetOrderByOrderIdStringAsync(orderIdString);
        }

        public async Task UpdateOrderStatusAsync(int orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0)
        {
            try
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null)
                    return;

                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // 累加成交数量并计算加权平均价
                if (filledQuantity > 0)
                {
                    var previousFilled = order.FilledQuantity;
                    var previousAvg = order.AveragePrice;
                    var newFilled = previousFilled + filledQuantity;

                    if (averagePrice > 0)
                    {
                        if (previousFilled <= 0)
                        {
                            order.AveragePrice = averagePrice; // 首次成交
                        }
                        else
                        {
                            order.AveragePrice = (previousAvg * previousFilled + averagePrice * filledQuantity) / newFilled;
                        }
                    }

                    order.FilledQuantity = newFilled;

                    // 自动推导状态
                    if (newFilled >= order.Quantity && order.Quantity > 0)
                    {
                        status = OrderStatus.Filled;
                    }
                    else if (newFilled > 0 && status != OrderStatus.Cancelled && status != OrderStatus.Rejected)
                    {
                        status = OrderStatus.PartiallyFilled;
                    }
                }

                // 如果没有成交新增但外部希望改状态（如 Cancelled / Rejected / Active），直接使用传入状态
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
                if (order == null || (userId.HasValue && order.UserId != userId.Value))
                    return false;

                if (order.Status != OrderStatus.Active && order.Status != OrderStatus.Pending)
                    return false;

                await UpdateOrderStatusAsync(orderId, OrderStatus.Cancelled);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订单时出错: OrderId={OrderId}, UserId={UserId}", orderId, userId);
                throw;
            }
        }

        public async Task<IEnumerable<Order>> GetOrdersForOrderBookAsync(string symbol, OrderSide side, int depth)
        {
            return await _orderRepository.GetOrdersForOrderBookAsync(symbol, side, depth);
        }

        public async Task<IEnumerable<Order>> GetExpiredOrdersAsync(TimeSpan expireAfter)
        {
            var expireTime = DateTimeOffset.UtcNow.Add(-expireAfter).ToUnixTimeMilliseconds();
            return await _orderRepository.FindAsync(o => o.CreatedAt < expireTime && 
                                                       (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Active));
        }

        private string GenerateOrderId()
        {
            return $"ORD_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000, 9999)}";
        }
    }
}
