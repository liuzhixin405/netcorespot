using System.Collections.Concurrent;
using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Interfaces.Trading; // added for IOrderBookSnapshotCache
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.API.Services
{
    /// <summary>
    /// 连接 IMarketDataStreamProvider (OKX 等) 并将事件转换为现有 SignalR 推送
    /// </summary>
    public class MarketDataStreamRelayService : BackgroundService
    {
        private readonly ILogger<MarketDataStreamRelayService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IEnumerable<IMarketDataStreamProvider> _streamProviders;
        private readonly string[] _symbols = new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" }; // 可配置
        private readonly string[] _klineIntervals = new[] { "1m" }; // MVP 只推 1m

        private readonly ConcurrentDictionary<string, int> _symbolIdCache = new();
        // 新增: 订单簿状态缓存 (Hash + 上次推送时间戳ms)
        private readonly ConcurrentDictionary<string, (string Hash, long LastPushMs)> _orderBookState = new();
        private const int OrderBookMinPushIntervalMs = 250; // 最小推送间隔

        public MarketDataStreamRelayService(
            ILogger<MarketDataStreamRelayService> logger,
            IServiceScopeFactory scopeFactory,
            IEnumerable<IMarketDataStreamProvider> streamProviders)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _streamProviders = streamProviders;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_streamProviders.Any())
            {
                _logger.LogWarning("没有注册任何 IMarketDataStreamProvider，跳过启动");
                return;
            }

            foreach (var provider in _streamProviders)
            {
                HookProvider(provider, stoppingToken);
                try
                {
                    await provider.ConnectAsync(stoppingToken);
                    foreach (var symbol in _symbols)
                    {
                        await provider.SubscribeTickerAsync(symbol, stoppingToken);
                        await provider.SubscribeOrderBookAsync(symbol, 5, stoppingToken);
                        await provider.SubscribeTradesAsync(symbol, stoppingToken);
                        foreach (var itv in _klineIntervals)
                        {
                            await provider.SubscribeKLineAsync(symbol, itv, stoppingToken);
                        }
                    }
                    _logger.LogInformation("已启动流式行情提供者: {Name}", provider.ProviderName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "启动 {Name} 失败", provider.ProviderName);
                }
            }

            // 保持后台运行
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        private void HookProvider(IMarketDataStreamProvider provider, CancellationToken ct)
        {
            provider.OnTicker += ticker => _ = Task.Run(() => RelayTickerAsync(ticker, ct), ct);
            provider.OnOrderBook += ob => _ = Task.Run(() => RelayOrderBookAsync(ob, ct), ct);
            provider.OnKLine += k => _ = Task.Run(() => RelayKLineAsync(k, ct), ct);
            provider.OnTrade += trade => { /* 目前未单独推逐笔，可后续扩展 */ };
        }

        private async Task RelayTickerAsync(MarketTicker t, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var push = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
                var priceService = scope.ServiceProvider.GetService<IPriceDataService>();
                Task? updateTask = null;
                if (priceService != null)
                {
                    // 需要等待执行完成以防止作用域提前释放导致 DbContext 被处置
                    updateTask = priceService.UpdateTradingPairPriceAsync(t.Symbol, t.Last, t.ChangePercent, t.Volume24h, t.High24h, t.Low24h);
                }
                var priceData = new
                {
                    symbol = t.Symbol,
                    price = t.Last,
                    change24h = t.ChangePercent, // 小数形式: 0.0123 = +1.23%
                    volume24h = t.Volume24h,
                    high24h = t.High24h,
                    low24h = t.Low24h,
                    timestamp = t.Ts
                };
                _logger.LogDebug("Ticker Relay {Symbol} price={Price} change%={Change} vol={Vol}", t.Symbol, t.Last, t.ChangePercent, t.Volume24h);
                if (updateTask != null)
                {
                    // 与推送并行执行，减少等待时间
                    await Task.WhenAll(updateTask, push.PushPriceDataAsync(t.Symbol, priceData));
                }
                else
                {
                    await push.PushPriceDataAsync(t.Symbol, priceData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RelayTicker 失败 {Symbol}", t.Symbol);
            }
        }

        private async Task RelayOrderBookAsync(OrderBookDelta delta, CancellationToken ct)
        {
            try
            {
                // 计算当前订单簿哈希（简单拼接价格与数量）
                var hash = ComputeOrderBookHash(delta.Bids, delta.Asks);
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var state = _orderBookState.GetOrAdd(delta.Symbol, _ => ("", 0));

                // 去重: 哈希相同则直接忽略
                if (state.Hash == hash)
                {
                    return;
                }

                // 节流: 距离上次推送不足阈值则暂不推送，只更新缓存哈希（等待下一窗口）
                if (nowMs - state.LastPushMs < OrderBookMinPushIntervalMs)
                {
                    _orderBookState[delta.Symbol] = (hash, state.LastPushMs);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var push = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
                var snapshotCache = scope.ServiceProvider.GetService<IOrderBookSnapshotCache>();

                // 首次推送 或 距离上次推送时间较长(>3s) 发送快照，否则发送增量 (当前 books5 直接作为快照)
                bool pushAsSnapshot = state.LastPushMs == 0 || (nowMs - state.LastPushMs) > 3000 || delta.IsSnapshot;
                if (pushAsSnapshot)
                {
                    await push.PushExternalOrderBookSnapshotAsync(delta.Symbol, delta.Bids.ToList(), delta.Asks.ToList(), nowMs);
                    snapshotCache?.Update(delta.Symbol, delta.Bids, delta.Asks, nowMs);
                }
                else
                {
                    await push.PushOrderBookDeltaAsync(delta.Symbol, delta.Bids.ToList(), delta.Asks.ToList());
                }

                // 更新状态
                _orderBookState[delta.Symbol] = (hash, nowMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RelayOrderBook 失败 {Symbol}", delta.Symbol);
            }
        }

        private async Task RelayKLineAsync(KLineUpdate k, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var push = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
                var pairService = scope.ServiceProvider.GetService<IPriceDataService>();
                var klineService = scope.ServiceProvider.GetService<IKLineDataService>();
                int tradingPairId = await ResolveTradingPairIdAsync(k.Symbol, pairService, ct);
                if (tradingPairId == 0) return;
                var interval = k.Interval;
                var klineEntity = new KLineData
                {
                    TradingPairId = tradingPairId,
                    TimeFrame = interval,
                    OpenTime = k.OpenTime,
                    CloseTime = k.OpenTime + GetIntervalMs(interval),
                    Open = k.Open,
                    High = k.High,
                    Low = k.Low,
                    Close = k.Close,
                    Volume = k.Volume
                };
                if (klineService != null && k.IsClosed)
                {
                    // 必须等待完成，防止作用域释放后访问已处置的 DbContext
                    await klineService.AddOrUpdateKLineDataAsync(klineEntity);
                }
                await push.PushKLineDataAsync(k.Symbol, interval, klineEntity, k.IsClosed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RelayKLine 失败 {Symbol} {Interval}", k.Symbol, k.Interval);
            }
        }

        private async Task<int> ResolveTradingPairIdAsync(string symbol, IPriceDataService? service, CancellationToken ct)
        {
            if (_symbolIdCache.TryGetValue(symbol, out var id)) return id;
            if (service == null) return 0;
            var pair = await service.GetCurrentPriceAsync(symbol);
            if (pair?.Id > 0)
            {
                _symbolIdCache[symbol] = pair.Id;
                return pair.Id;
            }
            return 0;
        }

        private long GetIntervalMs(string interval)
        {
            return interval.ToLower() switch
            {
                "1m" => 60_000,
                "5m" => 5 * 60_000,
                "15m" => 15 * 60_000,
                "1h" => 60 * 60_000,
                "4h" => 4 * 60 * 60_000,
                "1d" => 24 * 60 * 60_000,
                _ => 60_000
            };
        }

        private string ComputeOrderBookHash(IReadOnlyList<CryptoSpot.Core.Interfaces.Trading.OrderBookLevel> bids, IReadOnlyList<CryptoSpot.Core.Interfaces.Trading.OrderBookLevel> asks)
        {
            // 只针对传入档位生成一个稳定字符串，用于快速比较；books5 数据量小，直接拼接即可
            var sb = new System.Text.StringBuilder();
            sb.Append('B');
            foreach (var b in bids)
            {
                sb.Append(b.Price).Append('|').Append(b.Quantity).Append(';');
            }
            sb.Append('A');
            foreach (var a in asks)
            {
                sb.Append(a.Price).Append('|').Append(a.Quantity).Append(';');
            }
            return sb.ToString();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var provider in _streamProviders)
            {
                try { await provider.DisconnectAsync(cancellationToken); } catch { }
            }
            await base.StopAsync(cancellationToken);
        }
    }
}
