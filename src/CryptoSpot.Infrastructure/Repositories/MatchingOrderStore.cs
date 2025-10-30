// filepath: g:\\github\\netcorespot\\src\\CryptoSpot.Infrastructure\\Repositories\\MatchingOrderStore.cs
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Repositories
{
    /// <summary>
    /// 撮合专用订单存取实现。
    /// </summary>
    public class MatchingOrderStore : IMatchingOrderStore
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<MatchingOrderStore> _logger;
        private readonly RedisOrderRepository? _redisOrderRepository;

        public MatchingOrderStore(IOrderRepository orderRepository,
                                  ITradingPairRepository tradingPairRepository,
                                  IUnitOfWork uow,
                                  ILogger<MatchingOrderStore> logger,
                                  RedisOrderRepository? redisOrderRepository = null)
        {
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _uow = uow;
            _logger = logger;
            _redisOrderRepository = redisOrderRepository;
        }

        public async Task<Order?> GetOrderAsync(int orderId)
        {
            // 优先尝试从 Redis 仓储获取（若可用），以便处理通过 Redis 创建但尚未写入 DB 的订单
            try
            {
                if (_redisOrderRepository != null)
                {
                    var redisOrder = await _redisOrderRepository.GetOrderByIdAsync(orderId);
                    if (redisOrder != null) return redisOrder;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "RedisOrderRepository.GetOrderByIdAsync failed for OrderId={OrderId}", orderId);
            }

            // 回退到数据库查询
            return await _orderRepository.GetByIdAsync(orderId);
        }

        public Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null) => _orderRepository.GetActiveOrdersAsync(symbol);

        public Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, OrderStatus? status = null, int limit = 100) => _orderRepository.GetUserOrdersAsync(userId, null, status, limit);

        public async Task<Order> AddOrderAsync(Order order)
        {
            // 优先尝试使用 RedisOrderRepository（运行时写入 Redis 并 enqueue 同步），若不可用则回退到 DB
            try
            {
                if (_redisOrderRepository != null)
                {
                    // 保存到 Redis（需要 symbol），尝试从 tradingPairId 获取 symbol
                    var tradingPair = await _tradingPairRepository.GetByIdAsync(order.TradingPairId);
                    var symbol = tradingPair?.Symbol ?? "BTCUSDT";
                    var created = await _redisOrderRepository.CreateOrderAsync(order, symbol);
                    _logger.LogInformation("[Redis] Order created via RedisOrderRepository: {OrderId}", created.Id);
                    return created;
                }
            }
            catch (Exception rex)
            {
                _logger.LogWarning(rex, "RedisOrderRepository failed, falling back to DB for AddOrderAsync");
            }

            var added = await _orderRepository.AddAsync(order);
            await _uow.SaveChangesAsync();
            _logger.LogInformation("[DB] Order created via DB: {OrderId}", added.Id);
            // Best-effort: if Redis repo available, seed the created DB order into Redis to keep cache warm
            try
            {
                if (_redisOrderRepository != null)
                {
                    var tradingPair = await _tradingPairRepository.GetByIdAsync(order.TradingPairId);
                    var symbol = tradingPair?.Symbol ?? "BTCUSDT";
                    // Ensure the order has an Id (DB assigned) - use added.Id
                    added.Id = added.Id; // no-op but clarify intent
                    await _redisOrderRepository.SeedOrderAsync(added, symbol);
                    _logger.LogInformation("[Backfill] Seeded DB order to Redis: {OrderId}", added.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to backfill DB order to Redis for OrderId={OrderId}", added.Id);
            }
            return added;
        }

        public async Task<bool> UpdateOrderStatusAsync(int orderId, OrderStatus status, decimal filledQuantityDelta = 0, decimal? averagePrice = null)
        {
            try
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null) return false;
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (filledQuantityDelta > 0)
                {
                    var newFilled = order.FilledQuantity + filledQuantityDelta;
                    if (averagePrice.HasValue && averagePrice > 0)
                    {
                        if (order.FilledQuantity <= 0)
                            order.AveragePrice = averagePrice.Value;
                        else
                            order.AveragePrice = (order.AveragePrice * order.FilledQuantity + averagePrice.Value * filledQuantityDelta) / newFilled;
                    }
                    order.FilledQuantity = newFilled;
                    if (newFilled >= order.Quantity) status = OrderStatus.Filled;
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
                        return true;
                    }
                    catch (Exception rex)
                    {
                        _logger.LogWarning(rex, "Redis UpdateOrderStatus failed, falling back to DB: OrderId={OrderId}", orderId);
                    }
                }

                await _orderRepository.UpdateAsync(order);
                await _uow.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateOrderStatus failed: {OrderId}", orderId);
                return false;
            }
        }

        public async Task<bool> CancelOrderAsync(int orderId)
        {
            try
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null) return false;
                if (order.Status != OrderStatus.Active && order.Status != OrderStatus.Pending && order.Status != OrderStatus.PartiallyFilled) return false;
                // Prefer Redis update when available
                if (_redisOrderRepository != null)
                {
                    try
                    {
                        await _redisOrderRepository.UpdateOrderStatusAsync(order.Id, OrderStatus.Cancelled, order.FilledQuantity);
                        return true;
                    }
                    catch (Exception rex)
                    {
                        _logger.LogWarning(rex, "Redis CancelOrder failed, falling back to DB: OrderId={OrderId}", orderId);
                    }
                }

                order.Status = OrderStatus.Cancelled;
                order.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _orderRepository.UpdateAsync(order);
                await _uow.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CancelOrder failed: {OrderId}", orderId);
                return false;
            }
        }
    }
}
