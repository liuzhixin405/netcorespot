using System.Collections.Concurrent;
using System.Threading.Channels;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.MatchEngine.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CryptoSpot.Persistence.Data;

namespace CryptoSpot.Infrastructure.MatchEngine.Services;

/// <summary>
/// 纯内存撮合引擎服务 - 基于 Channel，无 Redis 依赖
/// 实现了 Application 层的 IMatchEngineService 接口，供基础交易服务消费
/// </summary>
public class ChannelMatchEngineService : IMatchEngineService, IDisposable
{
    private readonly ILogger<ChannelMatchEngineService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMatchingAlgorithm _matchingAlgorithm;
    private readonly ITradingPairParser _pairParser;

    // 每个交易对一个订单簿和 Channel
    private readonly ConcurrentDictionary<string, IOrderBook> _orderBooks = new();
    private readonly ConcurrentDictionary<string, Channel<OrderRequest>> _orderChannels = new();
    private readonly ConcurrentDictionary<string, Task> _processingTasks = new();

    // 内存资产存储
    private readonly InMemoryAssetStore _assetStore;

    // 交易记录缓存（用于后续公共处理）
    private readonly ConcurrentDictionary<long, Trade> _trades = new();
    private long _nextTradeId = 1;

    private readonly CancellationTokenSource _cts = new();

    public ChannelMatchEngineService(
        ILogger<ChannelMatchEngineService> logger,
        IServiceProvider serviceProvider,
        IMatchingAlgorithm matchingAlgorithm,
        ITradingPairParser pairParser,
        InMemoryAssetStore assetStore)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _assetStore = assetStore;
        _matchingAlgorithm = matchingAlgorithm;
        _pairParser = pairParser;
    }

    /// <summary>
    /// 初始化交易对（启动时调用）
    /// </summary>
    public void InitializeSymbol(string symbol)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);

        if (!_orderBooks.TryAdd(normalizedSymbol, new InMemoryOrderBook(normalizedSymbol)))
        {
            _logger.LogDebug("Symbol {Symbol} already initialized", normalizedSymbol);
            return;
        }

        var channel = Channel.CreateBounded<OrderRequest>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        _orderChannels[normalizedSymbol] = channel;

        // 为每个交易对启动处理任务
        var task = Task.Run(() => ProcessOrdersAsync(normalizedSymbol, _cts.Token));
        _processingTasks[normalizedSymbol] = task;

        _logger.LogInformation("Initialized matching engine for symbol: {Symbol}", normalizedSymbol);
    }

    /// <summary>
    /// 下单接口（现有接口）
    /// </summary>
    public async Task<Order> PlaceOrderAsync(Order order, string symbol)
    {
        ValidateOrderInput(order, symbol);
        var normalizedSymbol = NormalizeSymbol(symbol);
        GetOrCreateOrderBook(normalizedSymbol);

        if (!_orderChannels.TryGetValue(normalizedSymbol, out var channel))
        {
            throw new InvalidOperationException($"Trading pair {normalizedSymbol} not initialized");
        }

        // 冻结资产
        var (currency, amount) = _pairParser.GetFreezeAmount(order, normalizedSymbol);
        var userId = order.UserId!.Value;

        if (!await _assetStore.FreezeAssetAsync(userId, currency, amount))
        {
            throw new InvalidOperationException($"余额不足：需要 {amount} {currency}");
        }

        // 设置订单状态
        order.Status = OrderStatus.Active;
        order.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        order.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 提交到 Channel
        var request = new OrderRequest { Order = order, Symbol = normalizedSymbol };
        await channel.Writer.WriteAsync(request, _cts.Token);

        return order;
    }

    public Task<OrderBookDepthDto?> GetOrderBookAsync(string symbol, int depth = 20)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return Task.FromResult<OrderBookDepthDto?>(null);
        }

        var normalizedSymbol = NormalizeSymbol(symbol);
        var orderBook = GetOrCreateOrderBook(normalizedSymbol);
        var sanitizedDepth = Math.Max(1, depth);

        var depthData = new OrderBookDepthDto
        {
            Symbol = normalizedSymbol,
            Timestamp = DateTime.UtcNow,
            Bids = BuildOrderBookLevels(orderBook.GetDepth(OrderSide.Buy, sanitizedDepth)),
            Asks = BuildOrderBookLevels(orderBook.GetDepth(OrderSide.Sell, sanitizedDepth))
        };

        return Task.FromResult<OrderBookDepthDto?>(depthData);
    }

    /// <summary>
    /// 处理特定交易对的订单流
    /// </summary>
    private async Task ProcessOrdersAsync(string symbol, CancellationToken ct)
    {
        var channel = _orderChannels[symbol];
        var orderBook = _orderBooks[symbol];

        _logger.LogInformation("Started order processor for {Symbol}", symbol);

        try
        {
            await foreach (var request in channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await ProcessSingleOrderAsync(request.Order, orderBook, symbol, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing order {OrderId} for {Symbol}",
                        request.Order.Id, symbol);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Order processor for {Symbol} stopped", symbol);
        }
    }

    /// <summary>
    /// 处理单个订单并执行撮合
    /// </summary>
    private async Task ProcessSingleOrderAsync(
        Order taker,
        IOrderBook orderBook,
        string symbol,
        CancellationToken ct)
    {
        var trades = new List<Trade>();

        // 添加到订单簿
        orderBook.Add(taker);

        // 执行撮合
        foreach (var matchSlice in _matchingAlgorithm.Match(orderBook, taker))
        {
            // 执行资产结算
            var settlementSuccess = await SettleTradeAsync(matchSlice, symbol);
            if (!settlementSuccess)
            {
                _logger.LogWarning(
                    "结算失败，终止撮合: Symbol={Symbol}, Maker={MakerId}, Taker={TakerId}",
                    symbol, matchSlice.Maker.Id, matchSlice.Taker.Id);
                break;
            }

            // 创建交易记录
            var trade = CreateTradeRecord(matchSlice, taker, symbol);
            trades.Add(trade);
            _trades[trade.Id] = trade;

            // 更新订单状态
            UpdateOrderStatus(matchSlice.Maker);
            UpdateOrderStatus(taker);

            // 发布交易事件
            await PublishTradeEventsAsync(symbol, trade, matchSlice.Maker, taker);

            // 吃单完全成交则终止
            if (taker.FilledQuantity >= taker.Quantity) break;
        }

        // 如果没有成交，发布订单簿更新事件
        if (trades.Count == 0)
        {
            await PublishOrderPlacedEventsAsync(symbol, taker);
        }

        // 持久化到数据库（异步，不阻塞撮合）
        _ = Task.Run(async () => await PersistToDatabaseAsync(taker, trades, symbol), ct);
    }

    /// <summary>
    /// 执行资产结算（纯内存）
    /// </summary>
    private async Task<bool> SettleTradeAsync(MatchSlice slice, string symbol)
    {
        try
        {
            var maker = slice.Maker;
            var taker = slice.Taker;
            var price = slice.Price;
            var quantity = slice.Quantity;
            var tradeAmount = price * quantity;

            var (baseCurrency, quoteCurrency) = _pairParser.ParseSymbol(symbol);

            var buyerId = taker.Side == OrderSide.Buy ? taker.UserId!.Value : maker.UserId!.Value;
            var sellerId = taker.Side == OrderSide.Buy ? maker.UserId!.Value : taker.UserId!.Value;

            await _assetStore.UnfreezeAssetAsync(buyerId, quoteCurrency, tradeAmount);
            await _assetStore.AddAvailableBalanceAsync(buyerId, baseCurrency, quantity);

            await _assetStore.UnfreezeAssetAsync(sellerId, baseCurrency, quantity);
            await _assetStore.AddAvailableBalanceAsync(sellerId, quoteCurrency, tradeAmount);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settlement failed for {Symbol}", symbol);
            return false;
        }
    }

    /// <summary>
    /// 创建交易记录
    /// </summary>
    private Trade CreateTradeRecord(MatchSlice slice, Order taker, string symbol)
    {
        var tradeId = Interlocked.Increment(ref _nextTradeId);
        var isTakerBuy = taker.Side == OrderSide.Buy;

        return new Trade
        {
            Id = tradeId,
            TradingPairId = taker.TradingPairId,
            BuyOrderId = isTakerBuy ? taker.Id : slice.Maker.Id,
            SellOrderId = isTakerBuy ? slice.Maker.Id : taker.Id,
            Price = slice.Price,
            Quantity = slice.Quantity,
            BuyerId = isTakerBuy ? taker.UserId ?? 0 : slice.Maker.UserId ?? 0,
            SellerId = isTakerBuy ? slice.Maker.UserId ?? 0 : taker.UserId ?? 0,
            ExecutedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TradeId = tradeId.ToString()
        };
    }

    /// <summary>
    /// 更新订单状态
    /// </summary>
    private static void UpdateOrderStatus(Order order)
    {
        if (order.FilledQuantity >= order.Quantity)
        {
            order.Status = OrderStatus.Filled;
        }
        else if (order.FilledQuantity > 0)
        {
            order.Status = OrderStatus.PartiallyFilled;
        }

        order.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 发布交易事件
    /// </summary>
    private async Task PublishTradeEventsAsync(
        string symbol,
        Trade trade,
        Order maker,
        Order taker)
    {
        _logger.LogInformation("Trade executed: Symbol={Symbol}, TradeId={TradeId}", symbol, trade.Id);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 发布订单下单事件
    /// </summary>
    private async Task PublishOrderPlacedEventsAsync(string symbol, Order order)
    {
        _logger.LogInformation("Order placed: Symbol={Symbol}, OrderId={OrderId}", symbol, order.Id);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 持久化到数据库（异步）
    /// </summary>
    private async Task PersistToDatabaseAsync(Order order, List<Trade> trades, string symbol)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var existingOrder = await dbContext.Orders.FindAsync(order.Id);
            if (existingOrder == null)
            {
                dbContext.Orders.Add(order);
            }
            else
            {
                existingOrder.FilledQuantity = order.FilledQuantity;
                existingOrder.Status = order.Status;
                existingOrder.UpdatedAt = order.UpdatedAt;
            }

            foreach (var trade in trades)
            {
                dbContext.Trades.Add(trade);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist to database: Order={OrderId}, Symbol={Symbol}",
                order.Id, symbol);
        }
    }

    private IOrderBook GetOrCreateOrderBook(string symbol)
    {
        if (!_orderBooks.TryGetValue(symbol, out var orderBook))
        {
            InitializeSymbol(symbol);
            orderBook = _orderBooks[symbol];
        }

        return orderBook;
    }

    private static string NormalizeSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol cannot be empty", nameof(symbol));
        }

        return symbol.Trim().ToUpperInvariant();
    }

    private static List<OrderBookLevelDto> BuildOrderBookLevels(IReadOnlyList<(decimal price, decimal quantity)> depthData)
    {
        var result = new List<OrderBookLevelDto>(depthData.Count);
        foreach (var (price, quantity) in depthData)
        {
            result.Add(new OrderBookLevelDto
            {
                Price = price,
                Quantity = quantity,
                Total = price * quantity,
                OrderCount = 0
            });
        }

        return result;
    }

    private static void ValidateOrderInput(Order order, string symbol)
    {
        if (order == null) throw new ArgumentNullException(nameof(order));
        if (string.IsNullOrEmpty(symbol)) throw new ArgumentNullException(nameof(symbol));
        if (order.UserId == null) throw new InvalidOperationException("订单缺少用户ID");
    }

    public void Dispose()
    {
        _cts.Cancel();

        foreach (var channel in _orderChannels.Values)
        {
            channel.Writer.Complete();
        }

        Task.WhenAll(_processingTasks.Values).Wait(TimeSpan.FromSeconds(5));

        _cts.Dispose();
    }

    private class OrderRequest
    {
        public Order Order { get; set; } = null!;
        public string Symbol { get; set; } = null!;
    }
}
