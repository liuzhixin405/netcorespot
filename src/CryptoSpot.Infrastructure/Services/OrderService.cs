using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Extensions;
using CryptoSpot.Core.Interfaces;
using CryptoSpot.Core.Interfaces.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoSpot.Infrastructure.Services
{
    public class OrderService : IOrderService, IDisposable
    {
        private readonly IRepository<Order> _orderRepository;
        private readonly ITradingPairService _tradingPairService;
        private readonly IDatabaseCoordinator _databaseCoordinator;
        private readonly ILogger<OrderService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public OrderService(
            IRepository<Order> orderRepository,
            ITradingPairService tradingPairService,
            IDatabaseCoordinator databaseCoordinator,
            ILogger<OrderService> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _orderRepository = orderRepository;
            _tradingPairService = tradingPairService;
            _databaseCoordinator = databaseCoordinator;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task<Order?> GetOrderByIdAsync(int orderId, int? userId = null)
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
            return await _databaseCoordinator.ExecuteAsync(async () =>
            {
                try
                {
                    if (string.IsNullOrEmpty(symbol))
                    {
                        return await _orderRepository.FindAsync(o => 
                            o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled);
                    }
                    
                    var tradingPairId = await _tradingPairService.GetTradingPairIdAsync(symbol);
                    if (tradingPairId == 0)
                    {
                        return new List<Order>();
                    }
                    
                    return await _orderRepository.FindAsync(o => 
                        o.TradingPairId == tradingPairId && 
                        (o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting active orders for {Symbol}", symbol ?? "all");
                    return new List<Order>();
                }
            }, $"GetActiveOrdersAsync_{symbol ?? "all"}");
        }


        public async Task<Order> CreateOrderAsync(int userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null)
        {
            try
            {
                var tradingPairId = await _tradingPairService.GetTradingPairIdAsync(symbol);
                if (tradingPairId == 0)
                {
                    throw new ArgumentException($"Trading pair {symbol} not found");
                }

                // 计算需要冻结的资产
                var baseAsset = symbol.Replace("USDT", "");
                var quoteAsset = "USDT";
                
                decimal requiredAssetAmount;
                string requiredAssetSymbol;
                
                if (side == OrderSide.Buy)
                {
                    // 买单需要冻结USDT
                    requiredAssetAmount = quantity * (price ?? 0);
                    requiredAssetSymbol = quoteAsset;
                }
                else
                {
                    // 卖单需要冻结基础资产
                    requiredAssetAmount = quantity;
                    requiredAssetSymbol = baseAsset;
                }

                // 检查并冻结用户资产
                using var scope = _serviceScopeFactory.CreateScope();
                var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();
                await assetService.FreezeAssetAsync(userId, requiredAssetSymbol, requiredAssetAmount);

                var order = new Order
                {
                    UserId = userId,
                    TradingPairId = tradingPairId,
                    OrderId = GenerateOrderId(),
                    Side = side,
                    Type = type,
                    Quantity = quantity,
                    Price = price,
                    Status = OrderStatus.Pending
                };

                var createdOrder = await _orderRepository.AddAsync(order);
                _logger.LogInformation("Created order {OrderId} for {Symbol}, froze {Amount} {Asset}", 
                    createdOrder.OrderId, symbol, requiredAssetAmount, requiredAssetSymbol);
                
                return createdOrder;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order for {Symbol}", symbol);
                throw;
            }
        }

        public async Task UpdateOrderStatusAsync(int orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0)
        {
            try
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null)
                {
                    return;
                }

                order.Status = status;
                order.Touch(); // 调用BaseEntity的Touch方法更新UpdatedAt
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

        public async Task<bool> CancelOrderAsync(int orderId, int? userId = null)
        {
            try
            {
                var order = await GetOrderByIdAsync(orderId, userId);
                if (order == null || (order.Status != OrderStatus.Pending && order.Status != OrderStatus.PartiallyFilled))
                {
                    return false;
                }

                // 解冻未成交部分的资产
                var tradingPair = await _tradingPairService.GetTradingPairByIdAsync(order.TradingPairId);
                if (tradingPair != null)
                {
                    var baseAsset = tradingPair.BaseAsset;
                    var quoteAsset = tradingPair.QuoteAsset;
                    
                    decimal unfreezeAmount;
                    string unfreezeAssetSymbol;
                    
                    if (order.Side == OrderSide.Buy)
                    {
                        // 买单解冻USDT
                        unfreezeAmount = order.RemainingQuantity * (order.Price ?? 0);
                        unfreezeAssetSymbol = quoteAsset;
                    }
                    else
                    {
                        // 卖单解冻基础资产
                        unfreezeAmount = order.RemainingQuantity;
                        unfreezeAssetSymbol = baseAsset;
                    }

                    // 解冻资产
                    using var scope = _serviceScopeFactory.CreateScope();
                    var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();
                    await assetService.UnfreezeAssetAsync(order.UserId!.Value, unfreezeAssetSymbol, unfreezeAmount);
                }

                await UpdateOrderStatusAsync(orderId, OrderStatus.Cancelled);
                _logger.LogInformation("Cancelled order {OrderId}, unfroze assets", orderId);
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
                var expireTime = DateTimeExtensions.GetCurrentUnixTimeMilliseconds() - (long)expireAfter.TotalMilliseconds;
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
            return $"ORD_{DateTime.Now:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";
        }

        public void Dispose()
        {
            // 没有需要释放的资源
        }
    }
}
