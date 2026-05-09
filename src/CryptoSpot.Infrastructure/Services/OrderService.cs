using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<OrderService> _logger;
        private readonly IDtoMappingService _mappingService;
        private readonly IAssetService _assetService;
        private readonly IMatchEngineService _matchEngine;

        public OrderService(
            IOrderRepository orderRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            ILogger<OrderService> logger,
            IDtoMappingService mappingService,
            IAssetService assetService,
            IMatchEngineService matchEngine)
        {
            _orderRepository = orderRepository;
            _tradingPairRepository = tradingPairRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _mappingService = mappingService;
            _assetService = assetService;
            _matchEngine = matchEngine;
        }

        public Task<ApiResponseDto<OrderDto?>> CreateOrderDtoAsync(long userId, string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null)
        {
            return ServiceHelper.ExecuteAsync<OrderDto?>(async () =>
            {
                var normalizedSymbol = symbol.Trim().ToUpperInvariant();
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(normalizedSymbol)
                    ?? throw new ArgumentException($"Trading pair {normalizedSymbol} does not exist");

                if (!tradingPair.IsActive)
                    throw new InvalidOperationException($"Trading pair {normalizedSymbol} is not active");
                if (!Enum.IsDefined(typeof(OrderSide), side))
                    throw new ArgumentException("Invalid order side", nameof(side));
                if (!Enum.IsDefined(typeof(OrderType), type))
                    throw new ArgumentException("Invalid order type", nameof(type));
                if (quantity <= 0)
                    throw new ArgumentException("Quantity must be greater than 0", nameof(quantity));
                if (type == OrderType.Limit && (!price.HasValue || price.Value <= 0))
                    throw new ArgumentException("Limit orders require a valid price", nameof(price));

                quantity = ServiceHelper.RoundDown(quantity, tradingPair.QuantityPrecision);
                if (type == OrderType.Limit && price.HasValue)
                    price = ServiceHelper.RoundDown(price.Value, tradingPair.PricePrecision);

                if (quantity <= 0 || (type == OrderType.Limit && price.HasValue && price.Value <= 0))
                    throw new ArgumentException("Quantity or price is invalid after precision rounding");
                if (tradingPair.MinQuantity > 0 && quantity < tradingPair.MinQuantity)
                    throw new ArgumentException($"Quantity must be at least {tradingPair.MinQuantity}");
                if (tradingPair.MaxQuantity > 0 && quantity > tradingPair.MaxQuantity)
                    throw new ArgumentException($"Quantity must be no more than {tradingPair.MaxQuantity}");

                var (freezeSymbol, freezeAmount) = GetRequiredFrozenAsset(tradingPair, side, type, quantity, price);
                var freezeResult = await _assetService.FreezeAssetAsync(userId, new AssetOperationRequestDto
                {
                    Symbol = freezeSymbol,
                    Amount = freezeAmount,
                    Remark = $"Freeze for {side} {type} order {normalizedSymbol}"
                });

                if (!freezeResult.Success)
                    throw new InvalidOperationException(freezeResult.Error ?? $"Insufficient {freezeSymbol} balance");

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

                try
                {
                    var createdOrder = await _orderRepository.AddAsync(order);
                    await _unitOfWork.SaveChangesAsync();
                    _logger.LogInformation(
                        "Order created: {OrderId} Symbol={Symbol} Status={Status} Frozen={Amount} {Asset}",
                        order.OrderId,
                        normalizedSymbol,
                        order.Status,
                        freezeAmount,
                        freezeSymbol);

                    // 提交到撮合引擎
                    try
                    {
                        await _matchEngine.EnqueueOrderAsync(createdOrder, normalizedSymbol);
                        _logger.LogInformation(
                            "Order submitted to match engine: {OrderId} Symbol={Symbol}",
                            createdOrder.OrderId, normalizedSymbol);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to submit order {OrderId} to match engine", createdOrder.OrderId);
                        // 不抛异常——订单已入库，撮合失败不影响下单成功
                    }

                    return _mappingService.MapToDto(createdOrder);
                }
                catch
                {
                    await _assetService.UnfreezeAssetAsync(userId, new AssetOperationRequestDto
                    {
                        Symbol = freezeSymbol,
                        Amount = freezeAmount,
                        Remark = $"Rollback freeze for failed order {normalizedSymbol}"
                    });
                    throw;
                }
            }, _logger, "Order creation failed");
        }

        public Task<ApiResponseDto<bool>> CancelOrderDtoAsync(long orderId, long? userId)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null || (userId.HasValue && order.UserId != userId.Value))
                    throw new InvalidOperationException("Order does not exist");
                if (order.Status != OrderStatus.Active && order.Status != OrderStatus.Pending && order.Status != OrderStatus.PartiallyFilled)
                    throw new InvalidOperationException("Order status cannot be cancelled");

                await ReleaseRemainingFrozenAssetAsync(order);
                await UpdateOrderStatusInternalAsync(order, OrderStatus.Cancelled);
                return true;
            }, _logger, "Order cancellation failed");
        }

        public Task<ApiResponseDto<IEnumerable<OrderDto>>> GetUserOrdersDtoAsync(long userId, OrderStatus? status = null, int limit = 100)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var orders = await _orderRepository.GetUserOrdersAsync(userId, null, status, limit);
                return _mappingService.MapToDto(orders);
            }, _logger, "Failed to get user orders");
        }

        public Task<ApiResponseDto<OrderDto?>> GetOrderByIdDtoAsync(long orderId, long? userId)
        {
            return ServiceHelper.ExecuteAsync<OrderDto?>(async () =>
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null || (userId.HasValue && order.UserId != userId.Value))
                    throw new InvalidOperationException("Order does not exist");
                return _mappingService.MapToDto(order);
            }, _logger, "Failed to get order");
        }

        public Task<ApiResponseDto<IEnumerable<OrderDto>>> GetActiveOrdersDtoAsync(string? symbol = null)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var orders = await _orderRepository.GetActiveOrdersAsync(symbol);
                return _mappingService.MapToDto(orders);
            }, _logger, "Failed to get active orders");
        }

        public Task<ApiResponseDto<bool>> UpdateOrderStatusDtoAsync(long orderId, OrderStatus status, decimal filledQuantity = 0, decimal averagePrice = 0)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var order = await _orderRepository.GetByIdAsync(orderId) ?? throw new InvalidOperationException("Order does not exist");
                await UpdateOrderStatusInternalAsync(order, status, filledQuantity, averagePrice);
                return true;
            }, _logger, "Failed to update order status");
        }

        public Task<ApiResponseDto<IEnumerable<OrderDto>>> GetExpiredOrdersDtoAsync(TimeSpan expireAfter)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var expireTime = ServiceHelper.NowMs() - (long)expireAfter.TotalMilliseconds;
                var orders = await _orderRepository.FindAsync(o => o.CreatedAt < expireTime && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Active));
                return _mappingService.MapToDto(orders);
            }, _logger, "Failed to get expired orders");
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

        private static (string Symbol, decimal Amount) GetRequiredFrozenAsset(
            TradingPair tradingPair,
            OrderSide side,
            OrderType type,
            decimal quantity,
            decimal? price)
        {
            if (side == OrderSide.Sell)
            {
                return (tradingPair.BaseAsset, quantity);
            }

            var effectivePrice = type == OrderType.Limit ? price!.Value : tradingPair.Price;
            if (effectivePrice <= 0)
                throw new ArgumentException("Market buy orders require a valid reference price");

            return (tradingPair.QuoteAsset, quantity * effectivePrice);
        }

        private async Task ReleaseRemainingFrozenAssetAsync(Order order)
        {
            var tradingPair = await _tradingPairRepository.GetByIdAsync(order.TradingPairId)
                ?? throw new InvalidOperationException("Trading pair does not exist");

            var remainingQuantity = Math.Max(0, order.Quantity - order.FilledQuantity);
            if (remainingQuantity <= 0 || !order.UserId.HasValue)
            {
                return;
            }

            var (assetSymbol, amount) = order.Side == OrderSide.Sell
                ? (tradingPair.BaseAsset, remainingQuantity)
                : (tradingPair.QuoteAsset, remainingQuantity * (order.Price ?? tradingPair.Price));

            if (amount <= 0)
            {
                return;
            }

            var unfreeze = await _assetService.UnfreezeAssetAsync(order.UserId.Value, new AssetOperationRequestDto
            {
                Symbol = assetSymbol,
                Amount = amount,
                Remark = $"Release remaining frozen balance for cancelled order {order.OrderId}"
            });

            if (!unfreeze.Success)
            {
                throw new InvalidOperationException(unfreeze.Error ?? $"Failed to release frozen {assetSymbol}");
            }
        }
    }
}
