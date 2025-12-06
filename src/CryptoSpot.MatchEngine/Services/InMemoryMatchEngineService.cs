using CryptoSpot.MatchEngine.Core;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.MatchEngine.Services;

/// <summary>
/// 内存撮合引擎服务
/// </summary>
public class InMemoryMatchEngineService
{
    private readonly ConcurrentDictionary<string, IOrderBook> _orderBooks;
    private readonly IMatchingAlgorithm _matchingAlgorithm;
    private readonly InMemoryAssetStore _assetStore;
    private readonly ILogger<InMemoryMatchEngineService> _logger;
    private long _nextOrderId = 1;
    private long _nextTradeId = 1;

    public InMemoryMatchEngineService(
        ConcurrentDictionary<string, IOrderBook> orderBooks,
        IMatchingAlgorithm matchingAlgorithm,
        InMemoryAssetStore assetStore,
        ILogger<InMemoryMatchEngineService> logger)
    {
        _orderBooks = orderBooks;
        _matchingAlgorithm = matchingAlgorithm;
        _assetStore = assetStore;
        _logger = logger;
    }

    /// <summary>
    /// 提交订单
    /// </summary>
    public Task<OrderMatchResult> SubmitOrderAsync(
        long userId,
        string symbol,
        string side,
        string type,
        decimal price,
        decimal quantity)
    {
        try
        {
            var orderId = Interlocked.Increment(ref _nextOrderId);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 创建订单
            var order = new CryptoSpot.Domain.Entities.Order
            {
                Id = orderId,
                UserId = userId,
                TradingPairId = 1, // 临时值，实际应从交易对映射获取
                OrderId = orderId.ToString(),
                Side = side.ToLower() == "buy" ? CryptoSpot.Domain.Entities.OrderSide.Buy : CryptoSpot.Domain.Entities.OrderSide.Sell,
                Type = type.ToLower() == "market" ? CryptoSpot.Domain.Entities.OrderType.Market : CryptoSpot.Domain.Entities.OrderType.Limit,
                Price = price,
                Quantity = quantity,
                FilledQuantity = 0,
                Status = CryptoSpot.Domain.Entities.OrderStatus.Pending,
                CreatedAt = timestamp
            };

            // 获取或创建订单簿
            var orderBook = _orderBooks.GetOrAdd(symbol, _ => new InMemoryOrderBook(symbol));

            // 执行撮合
            var matchSlices = _matchingAlgorithm.Match(orderBook, order);

            // 生成交易记录
            var trades = new List<TradeResult>();
            foreach (var slice in matchSlices)
            {
                var tradeId = Interlocked.Increment(ref _nextTradeId);
                trades.Add(new TradeResult
                {
                    Id = tradeId,
                    Price = slice.Price,
                    Quantity = slice.Quantity,
                    BuyOrderId = slice.Maker.Side == CryptoSpot.Domain.Entities.OrderSide.Buy ? slice.Maker.Id : slice.Taker.Id,
                    SellOrderId = slice.Maker.Side == CryptoSpot.Domain.Entities.OrderSide.Sell ? slice.Maker.Id : slice.Taker.Id,
                    Timestamp = timestamp
                });
            }

            _logger.LogInformation("订单撮合完成: OrderId={OrderId}, Symbol={Symbol}, ExecutedQty={ExecutedQty}, Trades={TradesCount}",
                orderId, symbol, order.FilledQuantity, trades.Count);

            return Task.FromResult(new OrderMatchResult
            {
                OrderId = orderId,
                Symbol = symbol,
                Status = order.Status.ToString(),
                ExecutedQuantity = order.FilledQuantity,
                Trades = trades
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "订单撮合失败: Symbol={Symbol}, Side={Side}", symbol, side);
            throw;
        }
    }

    /// <summary>
    /// 取消订单
    /// </summary>
    public async Task<bool> CancelOrderAsync(long orderId, long userId)
    {
        try
        {
            // 注意：当前 IOrderBook 接口不支持查找和取消订单
            // 需要维护一个订单ID到订单的映射，或扩展 IOrderBook 接口
            _logger.LogWarning("取消订单功能暂未实现: OrderId={OrderId}, UserId={UserId}", orderId, userId);
            return await Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消订单失败: OrderId={OrderId}", orderId);
            return false;
        }
    }

    /// <summary>
    /// 获取订单簿深度
    /// </summary>
    public object GetOrderBook(string symbol, int depth = 20)
    {
        if (_orderBooks.TryGetValue(symbol, out var orderBook))
        {
            var bids = orderBook.GetDepth(CryptoSpot.Domain.Entities.OrderSide.Buy, depth)
                .Select(d => new { Price = d.price, Quantity = d.quantity });
            var asks = orderBook.GetDepth(CryptoSpot.Domain.Entities.OrderSide.Sell, depth)
                .Select(d => new { Price = d.price, Quantity = d.quantity });
                
            return new
            {
                symbol,
                bids,
                asks,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        return new
        {
            symbol,
            bids = Array.Empty<object>(),
            asks = Array.Empty<object>(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}

/// <summary>
/// 订单撮合结果
/// </summary>
public class OrderMatchResult
{
    public long OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal ExecutedQuantity { get; set; }
    public List<TradeResult> Trades { get; set; } = new();
}

/// <summary>
/// 交易结果
/// </summary>
public class TradeResult
{
    public long Id { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public long BuyOrderId { get; set; }
    public long SellOrderId { get; set; }
    public long Timestamp { get; set; }
}
