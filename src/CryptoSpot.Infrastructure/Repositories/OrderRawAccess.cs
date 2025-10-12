// filepath: g:\\github\\netcorespot\\src\\CryptoSpot.Infrastructure\\Repositories\\MatchingOrderStore.cs
using CryptoSpot.Application.Abstractions.Repositories;
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

        public MatchingOrderStore(IOrderRepository orderRepository,
                                  ITradingPairRepository tradingPairRepository,
                                  IUnitOfWork uow,
                                  ILogger<MatchingOrderStore> logger)
        {
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _uow = uow;
            _logger = logger;
        }

        public Task<Order?> GetOrderAsync(int orderId) => _orderRepository.GetByIdAsync(orderId);

        public Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null) => _orderRepository.GetActiveOrdersAsync(symbol);

        public Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, OrderStatus? status = null, int limit = 100) => _orderRepository.GetUserOrdersAsync(userId, null, status, limit);

        public async Task<Order> AddOrderAsync(Order order)
        {
            var added = await _orderRepository.AddAsync(order);
            await _uow.SaveChangesAsync();
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
