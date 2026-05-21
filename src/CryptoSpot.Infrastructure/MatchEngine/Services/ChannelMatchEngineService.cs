using System.Collections.Concurrent;
using System.Threading.Channels;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.BackgroundServices;
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
public class ChannelMatchEngineService : IMatchEngineService, IAsyncDisposable
{
    private readonly ILogger<ChannelMatchEngineService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMatchingAlgorithm _matchingAlgorithm;
    private readonly ITradingPairParser _pairParser;
    private readonly IMatchEngineEventQueue _eventQueue;

    // 每个交易对一个订单簿和 Channel
    private readonly ConcurrentDictionary<string, IOrderBook> _orderBooks = new();
    private readonly ConcurrentDictionary<string, Channel<OrderRequest>> _orderChannels = new();
    private readonly ConcurrentDictionary<string, Task> _processingTasks = new();

    // 内存资产存储
    private readonly InMemoryAssetStore _assetStore;

    // 交易记录缓存（用于后续公共处理）
    private readonly ConcurrentDictionary<long, Trade> _trades = new();
    private long _nextTradeId = 1;
    private long _lastTradeCleanupTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private const long TradeCleanupIntervalMs = 300_000; // 5 minutes
    private const long TradeRetentionMs = 3_600_000; // 1 hour

    private readonly CancellationTokenSource _cts = new();
    private readonly Timer _tradeCleanupTimer;
    private readonly ConcurrentQueue<Task> _pendingPersistTasks = new();

    public ChannelMatchEngineService(
        ILogger<ChannelMatchEngineService> logger,
        IServiceScopeFactory scopeFactory,
        IMatchingAlgorithm matchingAlgorithm,
        ITradingPairParser pairParser,
        InMemoryAssetStore assetStore,
        IMatchEngineEventQueue eventQueue)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _assetStore = assetStore;
        _matchingAlgorithm = matchingAlgorithm;
        _pairParser = pairParser;
        _eventQueue = eventQueue;

        // 每 5 分钟清理过期交易缓存，不再在热路径上扫描
        _tradeCleanupTimer = new Timer(_ => TryCleanupOldTrades(), null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
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
        var task = Task.Run(async () =>
        {
            try
            {
                await ProcessOrdersAsync(normalizedSymbol, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Match engine channel for {Symbol} cancelled", normalizedSymbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Match engine channel for {Symbol} faulted", normalizedSymbol);
            }
        });
        _processingTasks[normalizedSymbol] = task;

        _logger.LogInformation("Initialized matching engine for symbol: {Symbol}", normalizedSymbol);
    }

    /// <summary>
    /// 下单接口（完整流程：冻结资产 + 入队撮合）
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

        order.Status = OrderStatus.Active;
        order.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        order.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var request = new OrderRequest { Type = OrderRequestType.Place, Order = order, Symbol = normalizedSymbol };
        await channel.Writer.WriteAsync(request, _cts.Token);

        return order;
    }

    /// <summary>
    /// 直接入队撮合（资产已由调用方在 DB 层冻结）
    /// </summary>
    public async Task EnqueueOrderAsync(Order order, string symbol)
    {
        ValidateOrderInput(order, symbol);
        var normalizedSymbol = NormalizeSymbol(symbol);
        GetOrCreateOrderBook(normalizedSymbol);

        if (!_orderChannels.TryGetValue(normalizedSymbol, out var channel))
        {
            throw new InvalidOperationException($"Trading pair {normalizedSymbol} not initialized");
        }

        order.Status = OrderStatus.Active;
        if (order.CreatedAt == 0)
            order.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        order.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 立即持久化 Active 状态，跟踪 Task 以便优雅关闭时等待
        var persistTask = PersistActiveStatusAsync(order);
        _pendingPersistTasks.Enqueue(persistTask);
        _ = persistTask.ContinueWith(_ => CleanupPersistTask(persistTask),
            TaskContinuationOptions.ExecuteSynchronously);

        var request = new OrderRequest { Type = OrderRequestType.Place, Order = order, Symbol = normalizedSymbol };
        await channel.Writer.WriteAsync(request, _cts.Token);

        _logger.LogDebug("Order {OrderId} enqueued for {Symbol}", order.OrderId, normalizedSymbol);
    }

    public async Task<bool> CancelOrderAsync(long orderId, string symbol)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        GetOrCreateOrderBook(normalizedSymbol);

        if (!_orderChannels.TryGetValue(normalizedSymbol, out var channel))
        {
            throw new InvalidOperationException($"Trading pair {normalizedSymbol} not initialized");
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new OrderRequest
        {
            Type = OrderRequestType.Cancel,
            OrderId = orderId,
            Symbol = normalizedSymbol,
            Completion = completion
        };

        await channel.Writer.WriteAsync(request, _cts.Token);
        return await completion.Task.WaitAsync(_cts.Token);
    }

    public void RestoreOpenOrder(Order order, string symbol)
    {
        ValidateOrderInput(order, symbol);
        if (order.FilledQuantity >= order.Quantity)
        {
            return;
        }

        var normalizedSymbol = NormalizeSymbol(symbol);
        var orderBook = GetOrCreateOrderBook(normalizedSymbol);
        orderBook.Add(order);
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
                    if (request.Type == OrderRequestType.Cancel)
                    {
                        ProcessCancelOrder(request, orderBook, symbol);
                        continue;
                    }

                    if (request.Order == null)
                    {
                        _logger.LogWarning("Skipping empty order request for {Symbol}", symbol);
                        continue;
                    }

                    await ProcessSingleOrderAsync(request.Order, orderBook, symbol, ct);
                }
                catch (Exception ex)
                {
                    request.Completion?.TrySetException(ex);
                    _logger.LogError(ex, "Error processing order request for {Symbol}", symbol);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Order processor for {Symbol} stopped", symbol);
        }
    }

    private void ProcessCancelOrder(OrderRequest request, IOrderBook orderBook, string symbol)
    {
        var removed = orderBook.RemoveById(request.OrderId);
        request.Completion?.TrySetResult(removed);
        _logger.LogDebug("Cancel request processed for {Symbol}: OrderId={OrderId}, Removed={Removed}",
            symbol, request.OrderId, removed);
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
        var affectedMakers = new HashSet<Order>(); // 追踪被修改的 maker

        _logger.LogDebug(
            "Processing order {OrderId}: Side={Side}, Type={Type}, Price={Price}, Qty={Qty}, UserId={UserId}",
            taker.OrderId, taker.Side, taker.Type, taker.Price, taker.Quantity, taker.UserId);

        orderBook.Add(taker);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Order {OrderId} added to book. Bids={Bids}, Asks={Asks}",
                taker.OrderId,
                orderBook.GetDepth(OrderSide.Buy, 5).Count,
                orderBook.GetDepth(OrderSide.Sell, 5).Count);
        }

        int matchCount = 0;
        foreach (var matchSlice in _matchingAlgorithm.Match(orderBook, taker))
        {
            matchCount++;
            var settlementSuccess = await SettleTradeAsync(matchSlice, symbol);
            if (!settlementSuccess)
            {
                _logger.LogWarning(
                    "结算失败，终止撮合: Symbol={Symbol}, Maker={MakerId}, Taker={TakerId}",
                    symbol, matchSlice.Maker.Id, matchSlice.Taker.Id);
                break;
            }

            var qty = matchSlice.Quantity;
            taker.FilledQuantity += qty;
            matchSlice.Maker.FilledQuantity += qty;

            if (matchSlice.Maker.FilledQuantity >= matchSlice.Maker.Quantity)
            {
                orderBook.Remove(matchSlice.Maker);
            }

            var trade = CreateTradeRecord(matchSlice, taker, symbol);
            trades.Add(trade);
            var memId = Interlocked.Increment(ref _nextTradeId);
            _trades[memId] = trade;

            UpdateOrderStatus(matchSlice.Maker);
            UpdateOrderStatus(taker);
            affectedMakers.Add(matchSlice.Maker);

            await PublishTradeEventsAsync(symbol, trade, matchSlice.Maker, taker);

            if (taker.FilledQuantity >= taker.Quantity) break;
        }

        if (taker.FilledQuantity >= taker.Quantity)
        {
            orderBook.Remove(taker);
        }

        _logger.LogDebug(
            "Order {OrderId} matching complete: Matches={Matches}, TakerFill={Filled}/{Qty}, Status={Status}",
            taker.OrderId, matchCount, taker.FilledQuantity, taker.Quantity, taker.Status);

        if (trades.Count == 0)
        {
            _logger.LogDebug("Order {OrderId} had no trades, staying in book", taker.OrderId);
            await PublishOrderPlacedEventsAsync(symbol, taker);
        }

        // 持久化 taker
        await PersistToDatabaseAsync(taker, trades, symbol);

        // 批量持久化所有被修改的 maker（单次 DB 往返）
        if (affectedMakers.Count > 0)
        {
            await PersistOrderStatusBatchAsync(affectedMakers);
        }
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
    /// 创建交易记录（Id=0 让数据库自增，避免主键冲突）
    /// </summary>
    private Trade CreateTradeRecord(MatchSlice slice, Order taker, string symbol)
    {
        var tradeId = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Interlocked.Increment(ref _nextTradeId):x}";
        var isTakerBuy = taker.Side == OrderSide.Buy;

        return new Trade
        {
            Id = 0, // 数据库自增，避免 Duplicate entry
            TradingPairId = taker.TradingPairId,
            BuyOrderId = isTakerBuy ? taker.Id : slice.Maker.Id,
            SellOrderId = isTakerBuy ? slice.Maker.Id : taker.Id,
            Price = slice.Price,
            Quantity = slice.Quantity,
            BuyerId = isTakerBuy ? taker.UserId ?? 0 : slice.Maker.UserId ?? 0,
            SellerId = isTakerBuy ? slice.Maker.UserId ?? 0 : taker.UserId ?? 0,
            ExecutedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TradeId = tradeId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    /// <summary>
    /// 更新订单状态
    /// </summary>
    private static void UpdateOrderStatus(Order order)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (order.FilledQuantity >= order.Quantity)
        {
            order.Status = OrderStatus.Filled;
        }
        else if (order.FilledQuantity > 0)
        {
            order.Status = OrderStatus.PartiallyFilled;
        }

        order.UpdatedAt = now;
    }

    /// <summary>
    /// 发布交易事件、ticker 数据和用户订单/资产更新
    /// </summary>
    private async Task PublishTradeEventsAsync(
        string symbol,
        Trade trade,
        Order maker,
        Order taker)
    {
        _logger.LogDebug("Trade executed: Symbol={Symbol}, TradeId={TradeId}", symbol, trade.Id);

        // 推送 ticker 数据
        var orderBook = _orderBooks.GetValueOrDefault(symbol);
        var bids = orderBook?.GetDepth(OrderSide.Buy, 1);
        var asks = orderBook?.GetDepth(OrderSide.Sell, 1);
        var bestBid = bids is { Count: > 0 } ? bids[0].price : (decimal?)null;
        var bestAsk = asks is { Count: > 0 } ? asks[0].price : (decimal?)null;
        var midPrice = bestBid.HasValue && bestAsk.HasValue
            ? (bestBid.Value + bestAsk.Value) / 2m
            : (decimal?)null;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _eventQueue.EnqueueAsync((push, _) => push.PushLastTradeAndMidPriceAsync(
            symbol, trade.Price, trade.Quantity,
            bestBid, bestAsk, midPrice, now));

        // 推送订单状态更新给用户
        await PushOrderUpdateToUserAsync(maker, symbol);
        await PushOrderUpdateToUserAsync(taker, symbol);

        // 推送成交给双方（各自的方向视角）
        if (maker.UserId.HasValue)
        {
            var makerUserId = maker.UserId.Value;
            var makerTrade = new TradeDto
            {
                Id = trade.Id, TradeId = trade.TradeId, Symbol = symbol,
                Price = trade.Price, Quantity = trade.Quantity,
                Fee = trade.Fee, FeeAsset = trade.FeeAsset,
                ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(trade.ExecutedAt).DateTime,
                Side = trade.BuyerId == maker.UserId ? Domain.Entities.OrderSide.Buy : Domain.Entities.OrderSide.Sell
            };
            await _eventQueue.EnqueueAsync((push, _) => push.PushUserTradeAsync(makerUserId, makerTrade));
        }
        if (taker.UserId.HasValue)
        {
            var takerUserId = taker.UserId.Value;
            var takerTrade = new TradeDto
            {
                Id = trade.Id, TradeId = trade.TradeId, Symbol = symbol,
                Price = trade.Price, Quantity = trade.Quantity,
                Fee = trade.Fee, FeeAsset = trade.FeeAsset,
                ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(trade.ExecutedAt).DateTime,
                Side = trade.BuyerId == taker.UserId ? Domain.Entities.OrderSide.Buy : Domain.Entities.OrderSide.Sell
            };
            await _eventQueue.EnqueueAsync((push, _) => push.PushUserTradeAsync(takerUserId, takerTrade));
        }
    }

    private async Task PushOrderUpdateToUserAsync(Order order, string symbol)
    {
        if (!order.UserId.HasValue) return;
        try
        {
            var dto = new OrderDto
            {
                Id = order.Id,
                OrderId = order.OrderId,
                Symbol = symbol,
                Side = order.Side,
                Type = order.Type,
                Quantity = order.Quantity,
                Price = order.Price,
                FilledQuantity = order.FilledQuantity,
                AveragePrice = order.AveragePrice,
                Status = order.Status,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(order.CreatedAt).DateTime,
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(order.UpdatedAt).DateTime
            };
            var userId = order.UserId.Value;
            await _eventQueue.EnqueueAsync((push, _) => push.PushUserOrderUpdateAsync(userId, dto));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "推送订单更新失败 OrderId={OrderId}", order.OrderId);
        }
    }

    private static string MapOrderStatus(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "pending",
        OrderStatus.Active => "active",
        OrderStatus.PartiallyFilled => "partiallyFilled",
        OrderStatus.Filled => "filled",
        OrderStatus.Cancelled => "cancelled",
        OrderStatus.Rejected => "rejected",
        _ => "pending"
    };

    /// <summary>
    /// 发布订单下单事件
    /// </summary>
    private async Task PublishOrderPlacedEventsAsync(string symbol, Order order)
    {
        _logger.LogDebug("Order placed: Symbol={Symbol}, OrderId={OrderId}", symbol, order.Id);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 仅持久化订单状态
    /// </summary>
    private async Task PersistOrderStatusBatchAsync(HashSet<Order> makers)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ids = makers.Select(m => m.Id).ToList();

            // 按 (Status, FilledQuantity, AveragePrice) 分组，减少 SQL 语句数
            foreach (var group in makers.GroupBy(m => new { m.Status, m.FilledQuantity, m.AveragePrice }))
            {
                var groupIds = group.Select(m => m.Id).ToList();
                var sample = group.First();
                await dbContext.Orders
                    .Where(o => groupIds.Contains(o.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.FilledQuantity, sample.FilledQuantity)
                        .SetProperty(o => o.Status, sample.Status)
                        .SetProperty(o => o.AveragePrice, sample.AveragePrice)
                        .SetProperty(o => o.UpdatedAt, sample.UpdatedAt));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch persist {Count} maker statuses", makers.Count);
        }
    }

    private async Task PersistActiveStatusAsync(Order order)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Orders
                .Where(o => o.Id == order.Id && o.Status == OrderStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, order.Status)
                    .SetProperty(o => o.UpdatedAt, order.UpdatedAt));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist Active status for order {OrderId}", order.OrderId);
        }
    }

    private async Task PersistOrderStatusAsync(Order order)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Orders
                .Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.FilledQuantity, order.FilledQuantity)
                    .SetProperty(o => o.Status, order.Status)
                    .SetProperty(o => o.AveragePrice, order.AveragePrice)
                    .SetProperty(o => o.UpdatedAt, order.UpdatedAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist order status for {OrderId}", order.OrderId);
        }
    }

    /// <summary>
    /// 持久化到数据库（异步）
    /// </summary>
    private async Task PersistToDatabaseAsync(Order order, List<Trade> trades, string symbol)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var strategy = dbContext.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync();

                if (order.Id > 0)
                {
                    var rows = await dbContext.Orders
                        .Where(o => o.Id == order.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(o => o.FilledQuantity, order.FilledQuantity)
                            .SetProperty(o => o.Status, order.Status)
                            .SetProperty(o => o.UpdatedAt, order.UpdatedAt));

                    if (rows == 0)
                    {
                        dbContext.Orders.Add(order);
                    }
                }
                else
                {
                    dbContext.Orders.Add(order);
                }

                foreach (var trade in trades)
                {
                    dbContext.Trades.Add(trade);
                }

                await dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });
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

    private void TryCleanupOldTrades()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastTradeCleanupTimestamp < TradeCleanupIntervalMs)
            return;

        _lastTradeCleanupTimestamp = now;
        var cutoff = now - TradeRetentionMs;
        var removed = 0;

        foreach (var key in _trades.Keys)
        {
            if (_trades.TryGetValue(key, out var trade) && trade.ExecutedAt < cutoff)
            {
                if (_trades.TryRemove(key, out _))
                    removed++;
            }
        }

        if (removed > 0)
            _logger.LogDebug("Cleaned up {Count} expired trades from memory cache", removed);
    }

    private void CleanupPersistTask(Task t)
    {
        // 清理队列中已完成的任务
        var pending = new List<Task>();
        while (_pendingPersistTasks.TryDequeue(out var task))
        {
            if (!task.IsCompleted)
                pending.Add(task);
        }
        foreach (var task in pending)
            _pendingPersistTasks.Enqueue(task);
    }

    public async ValueTask DisposeAsync()
    {
        _tradeCleanupTimer.Dispose();
        _cts.Cancel();

        // 等待所有挂起的持久化任务完成
        if (!_pendingPersistTasks.IsEmpty)
        {
            try
            {
                await Task.WhenAll(_pendingPersistTasks).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Pending persist tasks did not complete within timeout");
            }
        }

        foreach (var channel in _orderChannels.Values)
        {
            channel.Writer.Complete();
        }

        try
        {
            await Task.WhenAll(_processingTasks.Values).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Match engine processing tasks did not complete within timeout");
        }

        _cts.Dispose();
    }

    private enum OrderRequestType
    {
        Place,
        Cancel
    }

    private class OrderRequest
    {
        public OrderRequestType Type { get; set; }
        public Order? Order { get; set; }
        public long OrderId { get; set; }
        public string Symbol { get; set; } = null!;
        public TaskCompletionSource<bool>? Completion { get; set; }
    }
}
