// filepath: g:\\github\\netcorespot\\src\\CryptoSpot.Infrastructure\\Repositories\\OrderRawAccess.cs
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Repositories
{
    /// <summary>
    /// IOrderRawAccess 实现：直接使用底层 IOrderRepository，避免 DTO 映射。
    /// </summary>
    public class OrderRawAccess : IOrderRawAccess
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<OrderRawAccess> _logger;

        public OrderRawAccess(IOrderRepository orderRepository,
                              ITradingPairRepository tradingPairRepository,
                              IUnitOfWork uow,
                              ILogger<OrderRawAccess> logger)
        {
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _uow = uow;
            _logger = logger;
        }

        public Task<Order?> GetOrderRawAsync(int orderId) => _orderRepository.GetByIdAsync(orderId);

        public Task<IEnumerable<Order>> GetActiveOrdersRawAsync(string? symbol = null) => _orderRepository.GetActiveOrdersAsync(symbol);

        public Task<IEnumerable<Order>> GetUserOrdersRawAsync(int userId, OrderStatus? status = null, int limit = 100) => _orderRepository.GetUserOrdersAsync(userId, null, status, limit);

        public async Task<Order> AddOrderRawAsync(Order order)
        {
            var added = await _orderRepository.AddAsync(order);
            await _uow.SaveChangesAsync();
            return added;
        }

        public async Task<bool> UpdateOrderStatusRawAsync(int orderId, OrderStatus status, decimal filledQuantityDelta = 0, decimal? averagePrice = null)
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
                _logger.LogError(ex, "UpdateOrderStatusRaw failed: {OrderId}", orderId);
                return false;
            }
        }

        public async Task<bool> CancelOrderRawAsync(int orderId)
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
                _logger.LogError(ex, "CancelOrderRaw failed: {OrderId}", orderId);
                return false;
            }
        }
    }
}
