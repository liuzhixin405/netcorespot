using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class OrderService : IOrderService
    {
        private readonly IRepository<Order> _orderRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly ILogger<OrderService> _logger;

        public OrderService(
            IRepository<Order> orderRepository,
            ITradingPairRepository tradingPairRepository,
            ILogger<OrderService> logger)
        {
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _logger = logger;
        }

        public async Task<Order?> GetOrderByIdAsync(long orderId, int? userId = null)
        {
            try
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                
                // 如果指定了用户ID，验证订单所有权
                if (userId.HasValue && order?.UserId != userId.Value)
                {
                    return null;
                }
                
                return order;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order {OrderId}", orderId);
                return null;
            }
        }

        public async Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, OrderStatus? status = null, int limit = 100)
        {
            try
            {
                var orders = await _orderRepository.FindAsync(o => o.UserId == userId);
                
                if (status.HasValue)
                {
                    orders = orders.Where(o => o.Status == status.Value);
                }
                
                return orders.OrderByDescending(o => o.CreatedAt).Take(limit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user orders for user {UserId}", userId);
                return new List<Order>();
            }
        }

        public async Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null)
        {
            try
            {
                if (string.IsNullOrEmpty(symbol))
                {
                    return await _orderRepository.FindAsync(o => 
                        o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled);
                }
                
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (tradingPair == null)
                {
                    return new List<Order>();
                }
                
                return await _orderRepository.FindAsync(o => 
                    o.TradingPairId == tradingPair.Id && 
                    (o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active orders for {Symbol}", symbol ?? "all");
                return new List<Order>();
            }
        }

        public async Task<Order> CreateOrderAsync(int userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null)
        {
            return await CreateOrderAsync((int?)userId, symbol, side, type, quantity, price, null);
        }

        public async Task<Order> CreateOrderAsync(int? userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null, int? systemAccountId = null)
        {
            try
            {
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (tradingPair == null)
                {
                    throw new ArgumentException($"Trading pair {symbol} not found");
                }

                var order = new Order
                {
                    UserId = userId,
                    SystemAccountId = systemAccountId,
                    TradingPairId = tradingPair.Id,
                    OrderId = GenerateOrderId(),
                    Side = side,
                    Type = type,
                    Quantity = quantity,
                    Price = price,
                    Status = OrderStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                var createdOrder = await _orderRepository.AddAsync(order);
                _logger.LogInformation("Created order {OrderId} for {Symbol}", createdOrder.OrderId, symbol);
                
                return createdOrder;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order for {Symbol}", symbol);
                throw;
            }
        }

        public async Task UpdateOrderStatusAsync(long orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0)
        {
            try
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null)
                {
                    return;
                }

                order.Status = status;
                order.UpdatedAt = DateTime.UtcNow;
                order.FilledQuantity = filledQuantity;
                order.AveragePrice = averagePrice;

                await _orderRepository.UpdateAsync(order);
                _logger.LogDebug("Updated order {OrderId} status to {Status}", order.OrderId, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order {OrderId} status", orderId);
            }
        }

        public async Task<bool> CancelOrderAsync(long orderId, int? userId = null)
        {
            try
            {
                var order = await GetOrderByIdAsync(orderId, userId);
                if (order == null || (order.Status != OrderStatus.Pending && order.Status != OrderStatus.PartiallyFilled))
                {
                    return false;
                }

                await UpdateOrderStatusAsync(orderId, OrderStatus.Cancelled);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling order {OrderId}", orderId);
                return false;
            }
        }

        public async Task<IEnumerable<Order>> GetExpiredOrdersAsync(TimeSpan expireAfter)
        {
            try
            {
                var expireTime = DateTime.UtcNow - expireAfter;
                return await _orderRepository.FindAsync(o => 
                    (o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled) &&
                    o.CreatedAt < expireTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting expired orders");
                return new List<Order>();
            }
        }

        private string GenerateOrderId()
        {
            return $"ORD_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";
        }
    }
}
