using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Repositories.Redis;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services;

/// <summary>
/// Adapter that implements IOrderMatchingEngine by delegating order placement to IMatchEngineService.
/// This lets existing consumers that depend on IOrderMatchingEngine use the new match engine implementation.
/// </summary>
public class MatchEngineAdapter : IOrderMatchingEngine
{
    private readonly IMatchEngineService _matchEngine;
    private readonly RedisOrderRepository _redisOrders;
    private readonly IDtoMappingService _mapping;
    private readonly ILogger<MatchEngineAdapter> _logger;

    public MatchEngineAdapter(IMatchEngineService matchEngine, RedisOrderRepository redisOrders, IDtoMappingService mapping, ILogger<MatchEngineAdapter> logger)
    {
        _matchEngine = matchEngine;
        _redisOrders = redisOrders;
        _mapping = mapping;
        _logger = logger;
    }

    public async Task<OrderMatchResultDto> ProcessOrderAsync(CreateOrderRequestDto orderRequest, long userId = 0)
    {
        // Map request -> domain Order
        var tradingPairId = 0; // mapping service expects tradingPairId; some callers may set it later
        var orderDomain = _mapping.MapToDomain(orderRequest, userId, tradingPairId);

        try
        {
            var symbol = orderRequest.Symbol.ToUpper();
            var created = await _matchEngine.PlaceOrderAsync(orderDomain, symbol);

            var orderDto = _mapping.MapToDto(created);

            return new OrderMatchResultDto
            {
                Order = orderDto,
                Trades = new List<TradeDto>(),
                IsFullyMatched = created.Status == OrderStatus.Filled,
                TotalMatchedQuantity = created.FilledQuantity,
                AveragePrice = created.Price ?? 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MatchEngineAdapter failed to process order for symbol {Symbol}", orderRequest.Symbol);
            throw;
        }
    }

    public async Task<List<TradeDto>> MatchOrdersAsync(string symbol)
    {
        // Best-effort: reuse existing Redis-based manual matching logic by delegating to RedisOrderRepository + (no-op) matching.
        // For now we return empty list; specialized manual-match behavior can be implemented if needed.
        _logger.LogInformation("MatchOrdersAsync called for {Symbol} - adapter defers to match engine (no-op manual match)", symbol);
        return new List<TradeDto>();
    }

    public async Task<OrderBookDepthDto> GetOrderBookDepthAsync(string symbol, int depth = 20)
    {
        var result = new OrderBookDepthDto { Symbol = symbol, Bids = new List<OrderBookLevelDto>(), Asks = new List<OrderBookLevelDto>(), Timestamp = DateTime.UtcNow };
        try
        {
            var (bids, asks) = await _redisOrders.GetOrderBookDepthAsync(symbol, depth);
            result.Bids = bids.Select(b => new OrderBookLevelDto { Price = b.price, Quantity = b.quantity }).ToList();
            result.Asks = asks.Select(a => new OrderBookLevelDto { Price = a.price, Quantity = a.quantity }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get order book depth from Redis for {Symbol}", symbol);
        }

        return result;
    }

    public async Task<bool> CancelOrderAsync(long orderId, long userId = 0)
    {
        try
        {
            // Use RedisOrderRepository cancellation (existing behavior)
            return await _redisOrders.CancelOrderAsync(orderId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CancelOrderAsync failed for OrderId={OrderId}", orderId);
            return false;
        }
    }

    public Task<bool> CanMatchOrderAsync(OrderDto buyOrder, OrderDto sellOrder)
    {
        // Reuse simple checks
        if (buyOrder.Symbol != sellOrder.Symbol) return Task.FromResult(false);
        if (buyOrder.Side != OrderSide.Buy || sellOrder.Side != OrderSide.Sell) return Task.FromResult(false);
        if (buyOrder.Type == OrderType.Market || sellOrder.Type == OrderType.Market) return Task.FromResult(true);
        if (buyOrder.Price.HasValue && sellOrder.Price.HasValue) return Task.FromResult(buyOrder.Price.Value >= sellOrder.Price.Value);
        return Task.FromResult(false);
    }
}
