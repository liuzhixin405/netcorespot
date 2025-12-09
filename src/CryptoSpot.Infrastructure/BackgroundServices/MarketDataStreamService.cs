using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// 市场数据流后台服务
    /// 负责连接 OKX WebSocket，订阅行情数据，并通过 SignalR 推送给前端
    /// </summary>
    public class MarketDataStreamService : BackgroundService
    {
        private readonly IMarketDataStreamProvider _streamProvider;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<MarketDataStreamService> _logger;
        private readonly PriceUpdateBatchService _priceUpdateBatchService;

        // 默认订阅的交易对
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

            // 等待应用完全启动
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            try
            {
                // 注册事件处理器
                RegisterEventHandlers();

                // 连接 WebSocket
                await _streamProvider.ConnectAsync(stoppingToken);
                _logger.LogInformation("✅ 已连接到 {Provider} WebSocket", _streamProvider.ProviderName);

                // 订阅默认交易对
                await SubscribeDefaultSymbolsAsync(stoppingToken);

                // 保持服务运行
                while (!stoppingToken.IsCancellationRequested)
                {
                    // 检查连接状态
                    if (!_streamProvider.IsConnected)
                    {
                        _logger.LogWarning("⚠️ WebSocket 连接断开，尝试重连...");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        await _streamProvider.ConnectAsync(stoppingToken);
                        await SubscribeDefaultSymbolsAsync(stoppingToken);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
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

        private void RegisterEventHandlers()
        {
            // Ticker 事件 - 价格更新
            _streamProvider.OnTicker += ticker =>
            {
                try
                {
                    // 通过批量服务更新数据库
                    _priceUpdateBatchService.TryEnqueue(
                        ticker.Symbol,
                        ticker.Last,
                        ticker.ChangePercent,
                        ticker.Volume24h,
                        ticker.High24h,
                        ticker.Low24h);

                    // 推送到 SignalR
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = _serviceScopeFactory.CreateScope();
                            var pushService = scope.ServiceProvider.GetService<IRealTimeDataPushService>();
                            if (pushService != null)
                            {
                                var priceData = new
                                {
                                    symbol = ticker.Symbol,
                                    price = ticker.Last,
                                    change24h = ticker.ChangePercent,
                                    volume24h = ticker.Volume24h,
                                    high24h = ticker.High24h,
                                    low24h = ticker.Low24h,
                                    timestamp = ticker.Ts
                                };
                                await pushService.PushPriceDataAsync(ticker.Symbol, priceData);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "推送 Ticker 数据失败: {Symbol}", ticker.Symbol);
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "处理 Ticker 事件失败: {Symbol}", ticker.Symbol);
                }
            };

            // OrderBook 事件 - 订单簿更新
            _streamProvider.OnOrderBook += orderBook =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var pushService = scope.ServiceProvider.GetService<IRealTimeDataPushService>();
                        if (pushService != null)
                        {
                            var bids = orderBook.Bids.Select(b => new OrderBookLevelDto
                            {
                                Price = b.Price,
                                Quantity = b.Quantity,
                                Total = b.Price * b.Quantity
                            }).ToList();

                            var asks = orderBook.Asks.Select(a => new OrderBookLevelDto
                            {
                                Price = a.Price,
                                Quantity = a.Quantity,
                                Total = a.Price * a.Quantity
                            }).ToList();

                            await pushService.PushExternalOrderBookSnapshotAsync(
                                orderBook.Symbol,
                                bids,
                                asks,
                                orderBook.Ts);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "推送 OrderBook 数据失败: {Symbol}", orderBook.Symbol);
                    }
                });
            };

            // Trade 事件 - 成交更新
            _streamProvider.OnTrade += trade =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var pushService = scope.ServiceProvider.GetService<IRealTimeDataPushService>();
                        if (pushService != null)
                        {
                            var tradeDto = new MarketTradeDto
                            {
                                Id = trade.TradeId,
                                Symbol = trade.Symbol,
                                Price = trade.Price,
                                Quantity = trade.Quantity,
                                ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(trade.Ts).UtcDateTime,
                                IsBuyerMaker = trade.Side.Equals("sell", StringComparison.OrdinalIgnoreCase)
                            };
                            await pushService.PushTradeDataAsync(trade.Symbol, tradeDto);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "推送 Trade 数据失败: {Symbol}", trade.Symbol);
                    }
                });
            };

            // KLine 事件 - K线更新
            _streamProvider.OnKLine += kline =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var pushService = scope.ServiceProvider.GetService<IRealTimeDataPushService>();
                        if (pushService != null)
                        {
                            var klineDto = new KLineDataDto
                            {
                                OpenTime = kline.OpenTime,
                                Open = kline.Open,
                                High = kline.High,
                                Low = kline.Low,
                                Close = kline.Close,
                                Volume = kline.Volume,
                                CloseTime = kline.Ts
                            };
                            await pushService.PushKLineDataAsync(kline.Symbol, kline.Interval, klineDto, kline.IsClosed);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "推送 KLine 数据失败: {Symbol} {Interval}", kline.Symbol, kline.Interval);
                    }
                });
            };

            _logger.LogInformation("✅ 已注册市场数据事件处理器");
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

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MarketDataStreamService 正在停止...");

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
    }
}
