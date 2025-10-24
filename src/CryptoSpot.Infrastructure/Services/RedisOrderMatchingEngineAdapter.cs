using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.ValueObjects;
using CryptoSpot.Infrastructure.Repositories.Redis;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services;

/// <summary>
/// 适配器：将 RedisOrderMatchingEngine 适配到 IOrderMatchingEngine 接口
/// 使所有使用 IOrderMatchingEngine 的地方自动使用 Redis-First 架构
/// </summary>
public class RedisOrderMatchingEngineAdapter : IOrderMatchingEngine
{
    private readonly RedisOrderMatchingEngine _redisEngine;
    private readonly RedisOrderRepository _redisOrders;
    private readonly ILogger<RedisOrderMatchingEngineAdapter> _logger;

    public RedisOrderMatchingEngineAdapter(
        RedisOrderMatchingEngine redisEngine,
        RedisOrderRepository redisOrders,
        ILogger<RedisOrderMatchingEngineAdapter> logger)
    {
        _redisEngine = redisEngine;
        _redisOrders = redisOrders;
        _logger = logger;
    }

    /// <summary>
    /// 处理订单（下单）
    /// </summary>
    public async Task<OrderMatchResultDto> ProcessOrderAsync(CreateOrderRequestDto orderRequest, int userId = 0)
    {
        try
        {
            var symbol = orderRequest.Symbol.ToUpper();
            
            var order = new Order
            {
                UserId = userId,
                TradingPairId = 0,
                Side = orderRequest.Side,
                Type = orderRequest.Type,
                Price = orderRequest.Price,
                Quantity = orderRequest.Quantity,
                Status = OrderStatus.Active,
                FilledQuantity = 0,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var createdOrder = await _redisEngine.PlaceOrderAsync(order, symbol);

            var orderDto = new OrderDto
            {
                Id = createdOrder.Id,
                UserId = createdOrder.UserId,
                Symbol = symbol,
                TradingPairId = createdOrder.TradingPairId,
                Side = createdOrder.Side,
                Type = createdOrder.Type,
                Price = createdOrder.Price,
                Quantity = createdOrder.Quantity,
                FilledQuantity = createdOrder.FilledQuantity,
                RemainingQuantity = createdOrder.Quantity - createdOrder.FilledQuantity,
                Status = createdOrder.Status,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(createdOrder.CreatedAt).DateTime,
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(createdOrder.UpdatedAt).DateTime
            };

            return new OrderMatchResultDto
            {
                Order = orderDto,
                Trades = new List<TradeDto>(),
                IsFullyMatched = createdOrder.Status == OrderStatus.Filled,
                TotalMatchedQuantity = createdOrder.FilledQuantity,
                AveragePrice = createdOrder.Price ?? 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis matching engine order processing failed: {Symbol}", orderRequest.Symbol);
            throw;
        }
    }

    /// <summary>
    /// 手动触发撮合（通常由 Redis 引擎自动完成）
    /// </summary>
    public async Task<List<TradeDto>> MatchOrdersAsync(string symbol)
    {
        _logger.LogInformation("Manual match triggered: {Symbol} (Redis engine auto-matches)", symbol);
        
        return new List<TradeDto>();
    }

    /// <summary>
    /// 获取订单簿深度
    /// </summary>
    public async Task<OrderBookDepthDto> GetOrderBookDepthAsync(string symbol, int depth = 20)
    {
        try
        {
            var buyOrders = await _redisOrders.GetActiveOrdersAsync(symbol, OrderSide.Buy, depth * 2);
            var sellOrders = await _redisOrders.GetActiveOrdersAsync(symbol, OrderSide.Sell, depth * 2);

            // 聚合价格档位
            var bids = buyOrders
                .Where(o => o.Price.HasValue)
                .GroupBy(o => o.Price!.Value)
                .Select(g => new OrderBookLevelDto
                {
                    Price = g.Key,
                    Quantity = g.Sum(o => o.Quantity - o.FilledQuantity)
                })
                .OrderByDescending(x => x.Price)
                .Take(depth)
                .ToList();

            var asks = sellOrders
                .Where(o => o.Price.HasValue)
                .GroupBy(o => o.Price!.Value)
                .Select(g => new OrderBookLevelDto
                {
                    Price = g.Key,
                    Quantity = g.Sum(o => o.Quantity - o.FilledQuantity)
                })
                .OrderBy(x => x.Price)
                .Take(depth)
                .ToList();

            return new OrderBookDepthDto
            {
                Symbol = symbol,
                Bids = bids,
                Asks = asks,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Redis order book: {Symbol}", symbol);
            throw;
        }
    }

    /// <summary>
    /// 取消订单
    /// </summary>
    public async Task<bool> CancelOrderAsync(int orderId, int userId = 0)
    {
        try
        {
            var order = await _redisOrders.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Order not found: {OrderId}", orderId);
                return false;
            }

            if (userId > 0 && order.UserId != userId)
            {
                _logger.LogWarning("User {UserId} unauthorized to cancel order {OrderId}", userId, orderId);
                return false;
            }

            var symbol = await GetSymbolFromOrder(order);

            return await _redisEngine.CancelOrderAsync(orderId, userId, symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel Redis order: {OrderId}", orderId);
            return false;
        }
    }

    /// <summary>
    /// 检查两个订单是否可以撮合
    /// </summary>
    public Task<bool> CanMatchOrderAsync(OrderDto buyOrder, OrderDto sellOrder)
    {
        // 基础撮合逻辑
        if (buyOrder.Symbol != sellOrder.Symbol)
            return Task.FromResult(false);

        if (buyOrder.Side != OrderSide.Buy || sellOrder.Side != OrderSide.Sell)
            return Task.FromResult(false);

        // 市价单总是可以撮合
        if (buyOrder.Type == OrderType.Market || sellOrder.Type == OrderType.Market)
            return Task.FromResult(true);

        // 限价单：买单价格 >= 卖单价格
        if (buyOrder.Price.HasValue && sellOrder.Price.HasValue)
        {
            return Task.FromResult(buyOrder.Price.Value >= sellOrder.Price.Value);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// 辅助方法：从 Order 获取 Symbol
    /// </summary>
    private async Task<string> GetSymbolFromOrder(Order order)
    {
        var db = _redisOrders.GetDatabase();
        var orderKey = $"order:{order.Id}";
        var symbol = await db.HashGetAsync(orderKey, "symbol");
        
        if (symbol.HasValue && !string.IsNullOrEmpty(symbol.ToString()))
        {
            return symbol.ToString();
        }

        _logger.LogWarning("Order {OrderId} missing symbol field in Redis", order.Id);
        return "BTCUSDT";
    }
}
