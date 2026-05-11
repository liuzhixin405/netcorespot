using System.Collections.Concurrent;
using System.Threading.Channels;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Trading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.BackgroundServices
{
    public class MarketDataStreamService : BackgroundService
    {
        private readonly IMarketDataStreamProvider _streamProvider;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<MarketDataStreamService> _logger;
        private readonly PriceUpdateBatchService _priceUpdateBatchService;

        private readonly ConcurrentDictionary<string, long> _symbolToTradingPairId = new(StringComparer.OrdinalIgnoreCase);

        private readonly Channel<TickerEvent> _tickerChannel =
            Channel.CreateBounded<TickerEvent>(new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

        private readonly Channel<OrderBookEvent> _orderBookChannel =
            Channel.CreateBounded<OrderBookEvent>(new BoundedChannelOptions(2000)
            {
                FullMode = BoundedChannelFullMode.DropOldest
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

        private static readonly string[] DefaultSymbols = new[]
        {
            "BTCUSDT", "ETHUSDT", "BNBUSDT", "XRPUSDT", "ADAUSDT",
            "DOGEUSDT", "SOLUSDT", "DOTUSDT", "MATICUSDT", "LTCUSDT"
        };

        public MarketDataStreamService(
            IMarketDataStreamProvider streamProvider,
            IServiceScopeFactory serviceScopeFactory,
            PriceUpdateBatchService priceUpdateBatchService,
            ILogger<MarketDataStreamService> logger)
        {
            _streamProvider = streamProvider;
            _serviceScopeFactory = serviceScopeFactory;
            _priceUpdateBatchService = priceUpdateBatchService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("✅ MarketDataStreamService 正在启动...");

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            try
            {
                RegisterEventHandlers();

                await _streamProvider.ConnectAsync(stoppingToken);
                _logger.LogInformation("✅ 已连接到 {Provider} WebSocket", _streamProvider.ProviderName);

                await SubscribeDefaultSymbolsAsync(stoppingToken);

                // 每类事件一个消费 Task，不再 Fire-and-Forget
                var monitorTask = MonitorConnectionAsync(stoppingToken);
                var tickerTask = ProcessTickerChannelAsync(stoppingToken);
                var orderBookTask = ProcessOrderBookChannelAsync(stoppingToken);
                var tradeTask = ProcessTradeChannelAsync(stoppingToken);
                var klineTask = ProcessKLineChannelAsync(stoppingToken);

                await Task.WhenAll(monitorTask, tickerTask, orderBookTask, tradeTask, klineTask);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("MarketDataStreamService 正在停止...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ MarketDataStreamService 启动失败");
            }
        }

        private async Task MonitorConnectionAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                if (!_streamProvider.IsConnected)
                {
                    _logger.LogWarning("⚠️ WebSocket 连接断开，尝试重连...");
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    await _streamProvider.ConnectAsync(ct);
                    await SubscribeDefaultSymbolsAsync(ct);
                }
            }
        }

        private void RegisterEventHandlers()
        {
            _streamProvider.OnTicker += ticker =>
            {
                _priceUpdateBatchService.TryEnqueue(
                    ticker.Symbol, ticker.Last, ticker.ChangePercent,
                    ticker.Volume24h, ticker.High24h, ticker.Low24h);
                _tickerChannel.Writer.TryWrite(new TickerEvent(ticker));
            };

            _streamProvider.OnOrderBook += orderBook =>
            {
                _orderBookChannel.Writer.TryWrite(new OrderBookEvent(orderBook));
            };

            _streamProvider.OnTrade += trade =>
            {
                _tradeChannel.Writer.TryWrite(new TradeEvent(trade));
            };

            _streamProvider.OnKLine += kline =>
            {
                _klineChannel.Writer.TryWrite(new KLineEvent(kline));
            };

            _logger.LogInformation("✅ 已注册市场数据事件处理器");
        }

        private async Task ProcessTickerChannelAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var evt in _tickerChannel.Reader.ReadAllAsync(ct))
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var pushService = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
                        var priceData = new
                        {
                            symbol = evt.Ticker.Symbol,
                            price = evt.Ticker.Last,
                            change24h = evt.Ticker.ChangePercent,
                            volume24h = evt.Ticker.Volume24h,
                            high24h = evt.Ticker.High24h,
                            low24h = evt.Ticker.Low24h,
                            timestamp = evt.Ticker.Ts
                        };
                        await pushService.PushPriceDataAsync(evt.Ticker.Symbol, priceData);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "推送 Ticker 数据失败: {Symbol}", evt.Ticker.Symbol);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ProcessOrderBookChannelAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var evt in _orderBookChannel.Reader.ReadAllAsync(ct))
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var pushService = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
                        var bids = evt.OrderBook.Bids.Select(b => new OrderBookLevelDto
                        {
                            Price = b.Price, Quantity = b.Quantity, Total = b.Price * b.Quantity
                        }).ToList();
                        var asks = evt.OrderBook.Asks.Select(a => new OrderBookLevelDto
                        {
                            Price = a.Price, Quantity = a.Quantity, Total = a.Price * a.Quantity
                        }).ToList();
                        await pushService.PushExternalOrderBookSnapshotAsync(
                            evt.OrderBook.Symbol, bids, asks, evt.OrderBook.Ts);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "推送 OrderBook 数据失败: {Symbol}", evt.OrderBook.Symbol);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ProcessTradeChannelAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var evt in _tradeChannel.Reader.ReadAllAsync(ct))
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var pushService = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
                        var tradeDto = new MarketTradeDto
                        {
                            Id = evt.Trade.TradeId,
                            Symbol = evt.Trade.Symbol,
                            Price = evt.Trade.Price,
                            Quantity = evt.Trade.Quantity,
                            ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(evt.Trade.Ts).UtcDateTime,
                            IsBuyerMaker = evt.Trade.Side.Equals("sell", StringComparison.OrdinalIgnoreCase)
                        };
                        await pushService.PushTradeDataAsync(evt.Trade.Symbol, tradeDto);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "推送 Trade 数据失败: {Symbol}", evt.Trade.Symbol);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ProcessKLineChannelAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var evt in _klineChannel.Reader.ReadAllAsync(ct))
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var pushService = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
                        var klineDto = new KLineDataDto
                        {
                            OpenTime = evt.KLine.OpenTime,
                            Open = evt.KLine.Open, High = evt.KLine.High,
                            Low = evt.KLine.Low, Close = evt.KLine.Close,
                            Volume = evt.KLine.Volume, CloseTime = evt.KLine.Ts
                        };
                        await pushService.PushKLineDataAsync(evt.KLine.Symbol, evt.KLine.Interval, klineDto, evt.KLine.IsClosed);

                        if (evt.KLine.IsClosed)
                        {
                            var tradingPairId = await GetTradingPairIdAsync(evt.KLine.Symbol, scope);
                            if (tradingPairId > 0)
                            {
                                var klineRepo = scope.ServiceProvider.GetService<IKLineDataRepository>();
                                if (klineRepo != null)
                                {
                                    var entity = new Domain.Entities.KLineData
                                    {
                                        TradingPairId = tradingPairId,
                                        TimeFrame = evt.KLine.Interval,
                                        OpenTime = evt.KLine.OpenTime,
                                        Open = evt.KLine.Open, High = evt.KLine.High,
                                        Low = evt.KLine.Low, Close = evt.KLine.Close,
                                        Volume = evt.KLine.Volume,
                                        CloseTime = evt.KLine.Ts,
                                        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                        UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                    };
                                    await klineRepo.UpsertKLineDataAsync(entity);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "处理 KLine 事件失败: {Symbol} {Interval}",
                            evt.KLine.Symbol, evt.KLine.Interval);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task SubscribeDefaultSymbolsAsync(CancellationToken ct)
        {
            var symbols = await GetSubscribedSymbolsAsync(ct);

            foreach (var symbol in symbols)
            {
                try
                {
                    await _streamProvider.SubscribeTickerAsync(symbol, ct);
                    await _streamProvider.SubscribeOrderBookAsync(symbol, 5, ct);
                    await _streamProvider.SubscribeTradesAsync(symbol, ct);
                    await _streamProvider.SubscribeKLineAsync(symbol, "1m", ct);
                    _logger.LogDebug("已订阅 {Symbol} 的行情数据", symbol);
                    await Task.Delay(100, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "订阅 {Symbol} 失败", symbol);
                }
            }

            _logger.LogInformation("✅ 已订阅 {Count} 个交易对的行情数据", symbols.Count);
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
                if (symbols.Count > 0) return symbols;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取活动交易对失败，使用默认配置");
            }
            return DefaultSymbols;
        }

        private async Task<long> GetTradingPairIdAsync(string symbol, IServiceScope scope)
        {
            if (_symbolToTradingPairId.TryGetValue(symbol, out var cached))
                return cached;

            try
            {
                var repo = scope.ServiceProvider.GetService<ITradingPairRepository>();
                if (repo != null)
                {
                    var pair = await repo.GetBySymbolAsync(symbol);
                    if (pair != null)
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
            _tickerChannel.Writer.Complete();
            _orderBookChannel.Writer.Complete();
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

        private record TickerEvent(MarketTicker Ticker);
        private record OrderBookEvent(OrderBookDelta OrderBook);
        private record TradeEvent(PublicTrade Trade);
        private record KLineEvent(KLineUpdate KLine);
    }
}
