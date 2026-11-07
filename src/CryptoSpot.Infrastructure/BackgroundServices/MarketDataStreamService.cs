using System.Collections.Concurrent;
using System.Threading.Channels;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.MarketData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.BackgroundServices;

public class MarketDataStreamService : BackgroundService
{
    private readonly IMarketDataStreamProvider _streamProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<MarketDataStreamService> _logger;
    private readonly PriceUpdateBatchService _priceUpdateBatchService;
    private readonly IRealTimeDataPushService _pushService;
    private readonly MarketDataMetrics _metrics;
    private readonly MarketDataFeedOptions _options;

    private readonly ConcurrentDictionary<string, long> _symbolToTradingPairId = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _subscriptionGate = new(1, 1);

    private readonly ConcurrentDictionary<string, PendingOrderBookUpdate> _pendingOrderBooks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Channel<byte> _orderBookWake =
        Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    private readonly Channel<TradeEvent> _tradeChannel =
        Channel.CreateBounded<TradeEvent>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private readonly Channel<KLineEvent> _klineChannel =
        Channel.CreateBounded<KLineEvent>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private static readonly string[] DefaultSymbols = LocalMarketCatalog.Symbols;

    public MarketDataStreamService(
        IMarketDataStreamProvider streamProvider,
        IServiceScopeFactory serviceScopeFactory,
        PriceUpdateBatchService priceUpdateBatchService,
        IRealTimeDataPushService pushService,
        MarketDataMetrics metrics,
        MarketDataFeedOptions options,
        ILogger<MarketDataStreamService> logger)
    {
        _streamProvider = streamProvider;
        _serviceScopeFactory = serviceScopeFactory;
        _priceUpdateBatchService = priceUpdateBatchService;
        _pushService = pushService;
        _metrics = metrics;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MarketDataStreamService 正在启动...");

        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        try
        {
            RegisterEventHandlers();

            await _streamProvider.ConnectAsync(stoppingToken);
            _logger.LogInformation("已连接到 {Provider} WebSocket", _streamProvider.ProviderName);

            await SubscribeDefaultSymbolsAsync(stoppingToken);

            var monitorTask = MonitorConnectionAsync(stoppingToken);
            var orderBookTask = ProcessOrderBookChannelAsync(stoppingToken);
            var tradeTask = ProcessTradeChannelAsync(stoppingToken);
            var klineTask = ProcessKLineChannelAsync(stoppingToken);
            var refreshTask = RefreshSubscriptionsAsync(stoppingToken);

            await Task.WhenAll(monitorTask, orderBookTask, tradeTask, klineTask, refreshTask);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MarketDataStreamService 正在停止...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarketDataStreamService 启动失败");
        }
    }

    private async Task MonitorConnectionAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);

            if (_streamProvider.IsConnected)
            {
                continue;
            }

            _logger.LogWarning("WebSocket 连接断开，尝试重连...");
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            await _streamProvider.ConnectAsync(ct);

            if (_subscribedSymbols.Count == 0)
            {
                await SubscribeDefaultSymbolsAsync(ct);
            }
        }
    }

    private void RegisterEventHandlers()
    {
        _streamProvider.OnTicker += ticker =>
        {
            _metrics.TickerReceived();
            _priceUpdateBatchService.TryEnqueue(
                ticker.Symbol, ticker.Last, ticker.ChangePercent,
                ticker.Volume24h, ticker.High24h, ticker.Low24h);
        };

        _streamProvider.OnOrderBook += orderBook =>
        {
            _pendingOrderBooks.GetOrAdd(
                orderBook.Symbol,
                static _ => new PendingOrderBookUpdate()).Add(orderBook);
            _orderBookWake.Writer.TryWrite(0);
        };

        _streamProvider.OnTrade += trade =>
        {
            _tradeChannel.Writer.TryWrite(new TradeEvent(trade));
        };

        _streamProvider.OnKLine += kline =>
        {
            _klineChannel.Writer.TryWrite(new KLineEvent(kline));
        };

        _logger.LogInformation("已注册市场数据事件处理器");
    }

    private async Task ProcessOrderBookChannelAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _orderBookWake.Reader.ReadAllAsync(ct))
            {
                foreach (var (symbol, pendingUpdate) in _pendingOrderBooks)
                {
                    var update = pendingUpdate.Take();
                    if (update is null)
                    {
                        continue;
                    }

                    try
                    {
                        var bids = update.Bids.Select(b => new OrderBookLevelDto
                        {
                            Price = b.Price,
                            Quantity = b.Quantity,
                            Total = b.Price * b.Quantity
                        }).ToList();

                        var asks = update.Asks.Select(a => new OrderBookLevelDto
                        {
                            Price = a.Price,
                            Quantity = a.Quantity,
                            Total = a.Price * a.Quantity
                        }).ToList();

                        if (update.IsSnapshot)
                        {
                            await _pushService.PushExternalOrderBookSnapshotAsync(
                                symbol, bids, asks, update.Timestamp);
                        }
                        else
                        {
                            await _pushService.PushExternalOrderBookDeltaAsync(
                                symbol, bids, asks, update.Timestamp);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "推送 OrderBook 数据失败: {Symbol}", symbol);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessTradeChannelAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _tradeChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var tradeDto = new MarketTradeDto
                    {
                        Id = evt.Trade.TradeId,
                        Symbol = evt.Trade.Symbol,
                        Price = evt.Trade.Price,
                        Quantity = evt.Trade.Quantity,
                        ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(evt.Trade.Ts).UtcDateTime,
                        IsBuyerMaker = evt.Trade.Side.Equals("sell", StringComparison.OrdinalIgnoreCase)
                    };

                    await _pushService.PushTradeDataAsync(evt.Trade.Symbol, tradeDto);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "推送 Trade 数据失败: {Symbol}", evt.Trade.Symbol);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessKLineChannelAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _klineChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var klineDto = new KLineDataDto
                    {
                        Symbol = evt.KLine.Symbol,
                        TimeFrame = evt.KLine.Interval,
                        OpenTime = evt.KLine.OpenTime,
                        CloseTime = evt.KLine.CloseTime,
                        Open = evt.KLine.Open,
                        High = evt.KLine.High,
                        Low = evt.KLine.Low,
                        Close = evt.KLine.Close,
                        Volume = evt.KLine.Volume
                    };

                    await _pushService.PushKLineDataAsync(
                        evt.KLine.Symbol, evt.KLine.Interval, klineDto, evt.KLine.IsClosed);

                    if (!evt.KLine.IsClosed)
                    {
                        continue;
                    }

                    using var scope = _serviceScopeFactory.CreateScope();
                    var tradingPairId = await GetTradingPairIdAsync(evt.KLine.Symbol, scope, ct);
                    if (tradingPairId <= 0)
                    {
                        continue;
                    }

                    var klineRepo = scope.ServiceProvider.GetService<IKLineDataRepository>();
                    if (klineRepo is null)
                    {
                        continue;
                    }

                    var entity = new Domain.Entities.KLineData
                    {
                        TradingPairId = tradingPairId,
                        TimeFrame = evt.KLine.Interval,
                        OpenTime = evt.KLine.OpenTime,
                        CloseTime = evt.KLine.CloseTime,
                        Open = evt.KLine.Open,
                        High = evt.KLine.High,
                        Low = evt.KLine.Low,
                        Close = evt.KLine.Close,
                        Volume = evt.KLine.Volume
                    };

                    await klineRepo.UpsertKLineDataAsync(entity);
                    _metrics.KLineSaved();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "处理 KLine 事件失败: {Symbol} {Interval}",
                        evt.KLine.Symbol, evt.KLine.Interval);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SubscribeDefaultSymbolsAsync(CancellationToken ct)
    {
        var symbols = await GetSubscribedSymbolsAsync(ct);

        await _subscriptionGate.WaitAsync(ct);
        try
        {
            await _streamProvider.SubscribeBatchAsync(symbols, 10, "1m", ct);
            _subscribedSymbols.Clear();
            _subscribedSymbols.UnionWith(symbols);
        }
        finally
        {
            _subscriptionGate.Release();
        }

        _logger.LogInformation("已订阅 {Count} 个交易对的行情数据", symbols.Count);
    }

    private async Task RefreshSubscriptionsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.SymbolRefreshSeconds));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var symbols = await TryGetActiveSymbolsAsync(ct);
                if (symbols is null)
                {
                    continue;
                }

                var current = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);

                string[] removed;
                string[] added;

                await _subscriptionGate.WaitAsync(ct);
                try
                {
                    removed = _subscribedSymbols.Except(current, StringComparer.OrdinalIgnoreCase).ToArray();
                    added = current.Except(_subscribedSymbols, StringComparer.OrdinalIgnoreCase).ToArray();

                    if (added.Length > 0)
                    {
                        await _streamProvider.SubscribeBatchAsync(added, 10, "1m", ct);
                    }

                    if (removed.Length > 0)
                    {
                        await _streamProvider.UnsubscribeBatchAsync(removed, ct);
                        foreach (var symbol in removed)
                        {
                            _pendingOrderBooks.TryRemove(symbol, out _);
                            _pushService.RemoveExternalOrderBookSnapshot(symbol);
                        }
                    }

                    _subscribedSymbols.Clear();
                    _subscribedSymbols.UnionWith(current);
                }
                finally
                {
                    _subscriptionGate.Release();
                }

                foreach (var symbol in removed)
                {
                    _symbolToTradingPairId.TryRemove(symbol, out _);
                }

                if (added.Length > 0 || removed.Length > 0)
                {
                    _logger.LogInformation("行情订阅已刷新: 新增 {Added} 个, 移除 {Removed} 个", added.Length, removed.Length);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "刷新行情订阅失败");
            }
        }
    }

    private async Task<IReadOnlyList<string>> GetSubscribedSymbolsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var tradingPairRepo = scope.ServiceProvider.GetRequiredService<ITradingPairRepository>();
            var activePairs = await tradingPairRepo.GetActiveTradingPairsAsync();
            var symbols = activePairs
                .Select(tp => tp.Symbol?.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (symbols.Count > 0)
            {
                return symbols;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取活动交易对失败，使用默认配置");
        }

        return DefaultSymbols;
    }

    private async Task<IReadOnlyList<string>?> TryGetActiveSymbolsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var tradingPairRepo = scope.ServiceProvider.GetRequiredService<ITradingPairRepository>();
            var activePairs = await tradingPairRepo.GetActiveTradingPairsAsync();
            var symbols = activePairs
                .Select(tp => tp.Symbol?.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return symbols.Count > 0 ? symbols : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "刷新活动交易对失败，保留当前订阅");
            return null;
        }
    }

    private async Task<long> GetTradingPairIdAsync(string symbol, IServiceScope scope, CancellationToken ct)
    {
        if (_symbolToTradingPairId.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        try
        {
            var repo = scope.ServiceProvider.GetService<ITradingPairRepository>();
            if (repo is not null)
            {
                var pair = await repo.GetBySymbolAsync(symbol);
                if (pair is not null)
                {
                    _symbolToTradingPairId[symbol] = pair.Id;
                    return pair.Id;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查找交易对 {Symbol} ID 失败", symbol);
        }

        return 0;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MarketDataStreamService 正在停止...");
        _orderBookWake.Writer.Complete();
        _tradeChannel.Writer.Complete();
        _klineChannel.Writer.Complete();

        try
        {
            await _streamProvider.DisconnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "断开 WebSocket 连接时出错");
        }

        await base.StopAsync(cancellationToken);
    }

    private sealed class PendingOrderBookUpdate
    {
        private readonly object _sync = new();
        private Dictionary<decimal, OrderBookLevel> _bids = new();
        private Dictionary<decimal, OrderBookLevel> _asks = new();
        private Dictionary<decimal, OrderBookLevel> _publishedBids = new();
        private Dictionary<decimal, OrderBookLevel> _publishedAsks = new();
        private bool _hasSnapshot;
        private bool _isSnapshot;
        private bool _hasUpdate;
        private long _timestamp;

        public void Add(OrderBookDelta update)
        {
            lock (_sync)
            {
                if (update.IsSnapshot)
                {
                    _bids.Clear();
                    _asks.Clear();
                    _hasSnapshot = true;
                    _isSnapshot = true;
                }
                else if (!_hasSnapshot)
                {
                    return;
                }

                foreach (var bid in update.Bids)
                {
                    ApplyLevel(_bids, bid);
                }

                foreach (var ask in update.Asks)
                {
                    ApplyLevel(_asks, ask);
                }

                TrimLevels(_bids, bids: true);
                TrimLevels(_asks, bids: false);
                _timestamp = update.Ts;
                _hasUpdate = true;
            }
        }

        public PendingBookUpdate? Take()
        {
            lock (_sync)
            {
                if (!_hasUpdate)
                {
                    return null;
                }

                IReadOnlyList<OrderBookLevel> bids;
                IReadOnlyList<OrderBookLevel> asks;
                if (_isSnapshot)
                {
                    bids = SortLevels(_bids, bids: true);
                    asks = SortLevels(_asks, bids: false);
                    _publishedBids = new Dictionary<decimal, OrderBookLevel>(_bids);
                    _publishedAsks = new Dictionary<decimal, OrderBookLevel>(_asks);
                }
                else
                {
                    bids = GetChanges(_bids, _publishedBids);
                    asks = GetChanges(_asks, _publishedAsks);
                    _publishedBids = new Dictionary<decimal, OrderBookLevel>(_bids);
                    _publishedAsks = new Dictionary<decimal, OrderBookLevel>(_asks);
                }

                var update = new PendingBookUpdate(_isSnapshot, bids, asks, _timestamp);
                _isSnapshot = false;
                _hasUpdate = false;
                return update;
            }
        }

        private static void ApplyLevel(Dictionary<decimal, OrderBookLevel> levels, OrderBookLevel level)
        {
            if (level.Quantity <= 0)
            {
                levels.Remove(level.Price);
            }
            else
            {
                levels[level.Price] = level;
            }
        }

        private static void TrimLevels(Dictionary<decimal, OrderBookLevel> levels, bool bids)
        {
            while (levels.Count > 400)
            {
                var worstPrice = bids ? levels.Keys.Min() : levels.Keys.Max();
                levels.Remove(worstPrice);
            }
        }

        private static IReadOnlyList<OrderBookLevel> SortLevels(
            Dictionary<decimal, OrderBookLevel> levels,
            bool bids) =>
            bids
                ? levels.Values.OrderByDescending(level => level.Price).ToList()
                : levels.Values.OrderBy(level => level.Price).ToList();

        private static IReadOnlyList<OrderBookLevel> GetChanges(
            Dictionary<decimal, OrderBookLevel> current,
            Dictionary<decimal, OrderBookLevel> published)
        {
            var changes = new List<OrderBookLevel>();
            foreach (var (price, level) in current)
            {
                if (!published.TryGetValue(price, out var previous) || previous.Quantity != level.Quantity)
                {
                    changes.Add(level);
                }
            }

            foreach (var price in published.Keys)
            {
                if (!current.ContainsKey(price))
                {
                    changes.Add(new OrderBookLevel { Price = price, Quantity = 0 });
                }
            }

            return changes;
        }
    }

    private sealed record PendingBookUpdate(
        bool IsSnapshot,
        IReadOnlyList<OrderBookLevel> Bids,
        IReadOnlyList<OrderBookLevel> Asks,
        long Timestamp);
    private record TradeEvent(PublicTrade Trade);
    private record KLineEvent(KLineUpdate KLine);
}
