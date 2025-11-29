using System.Threading.Channels;
using CryptoSpot.Domain.Matching;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Matching;

/// <summary>
/// 纯内存撮合引擎服务
/// 使用 Channel 处理订单流，无需 Redis 和消息总线
/// </summary>
public sealed class InMemoryMatchingEngine : IDisposable
{
    private readonly Dictionary<string, OrderBook> _orderBooks = new();
    private readonly Dictionary<string, Channel<MatchOrder>> _orderChannels = new();
    private readonly Dictionary<string, Task> _processingTasks = new();
    private readonly Channel<LogBase> _logChannel;
    private readonly ILogger<InMemoryMatchingEngine> _logger;
    private readonly CancellationTokenSource _cts = new();
    
    // 事件回调
    public event Func<LogBase, Task>? OnLogGenerated;
    public event Func<OrderBookSnapshot, Task>? OnOrderBookUpdated;

    public InMemoryMatchingEngine(ILogger<InMemoryMatchingEngine> logger)
    {
        _logger = logger;
        _logChannel = Channel.CreateBounded<LogBase>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        
        // 启动日志处理器
        _ = Task.Run(() => ProcessLogsAsync(_cts.Token));
    }

    /// <summary>
    /// 初始化交易对的订单簿
    /// </summary>
    public void InitializeSymbol(string symbol, int baseScale = 8, int quoteScale = 2)
    {
        if (_orderBooks.ContainsKey(symbol))
        {
            _logger.LogWarning("Symbol {Symbol} already initialized", symbol);
            return;
        }

        var product = new TradingProduct
        {
            Symbol = symbol,
            BaseScale = baseScale,
            QuoteScale = quoteScale
        };

        var orderBook = new OrderBook(product);
        _orderBooks[symbol] = orderBook;

        var orderChannel = Channel.CreateBounded<MatchOrder>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        _orderChannels[symbol] = orderChannel;

        // 为每个交易对启动独立的处理任务
        var task = Task.Run(() => ProcessOrdersAsync(symbol, _cts.Token));
        _processingTasks[symbol] = task;

        _logger.LogInformation("Initialized matching engine for symbol: {Symbol}", symbol);
    }

    /// <summary>
    /// 提交订单到撮合引擎
    /// </summary>
    public async Task<bool> SubmitOrderAsync(MatchOrder order, CancellationToken ct = default)
    {
        if (!_orderChannels.TryGetValue(order.Symbol, out var channel))
        {
            _logger.LogError("Symbol {Symbol} not initialized", order.Symbol);
            return false;
        }

        try
        {
            await channel.Writer.WriteAsync(order, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit order {OrderId}", order.Id);
            return false;
        }
    }

    /// <summary>
    /// 获取订单簿快照
    /// </summary>
    public OrderBookSnapshot? GetOrderBook(string symbol)
    {
        return _orderBooks.TryGetValue(symbol, out var orderBook) 
            ? orderBook.GetSnapshot() 
            : null;
    }

    /// <summary>
    /// 处理特定交易对的订单流
    /// </summary>
    private async Task ProcessOrdersAsync(string symbol, CancellationToken ct)
    {
        var orderBook = _orderBooks[symbol];
        var channel = _orderChannels[symbol];

        _logger.LogInformation("Started order processor for {Symbol}", symbol);

        try
        {
            await foreach (var order in channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    IReadOnlyList<LogBase> logs;

                    // 处理取消订单
                    if (string.Equals(order.Status, "Cancelling", StringComparison.OrdinalIgnoreCase))
                    {
                        logs = orderBook.CancelOrder(order);
                    }
                    else
                    {
                        logs = orderBook.ApplyOrder(order);
                    }

                    // 发送日志到日志通道
                    foreach (var log in logs)
                    {
                        await _logChannel.Writer.WriteAsync(log, ct);
                    }

                    // 每处理完一个订单，发送订单簿快照
                    if (logs.Count > 0)
                    {
                        var snapshot = orderBook.GetSnapshot();
                        if (OnOrderBookUpdated != null)
                        {
                            await OnOrderBookUpdated.Invoke(snapshot);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing order {OrderId} for {Symbol}", 
                        order.Id, symbol);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Order processor for {Symbol} stopped", symbol);
        }
    }

    /// <summary>
    /// 处理撮合日志
    /// </summary>
    private async Task ProcessLogsAsync(CancellationToken ct)
    {
        _logger.LogInformation("Started log processor");

        try
        {
            await foreach (var log in _logChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    // 触发事件回调
                    if (OnLogGenerated != null)
                    {
                        await OnLogGenerated.Invoke(log);
                    }

                    // 记录日志
                    LogMatchResult(log);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing log {LogType}", log.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Log processor stopped");
        }
    }

    private void LogMatchResult(LogBase log)
    {
        switch (log)
        {
            case OpenLog open:
                _logger.LogInformation(
                    "[{Symbol}] OPEN: Order={OrderId} User={UserId} Side={Side} Price={Price} Size={Size}",
                    open.Symbol, open.Order.OrderId, open.Order.UserId, 
                    open.Order.Side, open.Order.Price, open.Order.Size);
                break;

            case MatchLog match:
                _logger.LogInformation(
                    "[{Symbol}] MATCH: Trade={TradeSeq} Taker={TakerId}(User={TakerUserId}) " +
                    "Maker={MakerId}(User={MakerUserId}) Price={Price} Size={Size}",
                    match.Symbol, match.TradeSeq, 
                    match.Taker.OrderId, match.Taker.UserId,
                    match.Maker.OrderId, match.Maker.UserId,
                    match.Price, match.Size);
                break;

            case DoneLog done:
                _logger.LogInformation(
                    "[{Symbol}] DONE: Order={OrderId} User={UserId} Reason={Reason} Remaining={Remaining}",
                    done.Symbol, done.Order.OrderId, done.Order.UserId, 
                    done.Reason, done.RemainingSize);
                break;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        
        foreach (var channel in _orderChannels.Values)
        {
            channel.Writer.Complete();
        }
        
        _logChannel.Writer.Complete();
        
        Task.WhenAll(_processingTasks.Values).Wait(TimeSpan.FromSeconds(5));
        
        _cts.Dispose();
    }
}
