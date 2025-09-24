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

        // Ticker 去重/节流缓存
        private readonly ConcurrentDictionary<string, (decimal Price, decimal Change, decimal Vol, decimal High, decimal Low, long LastPushMs, string Hash)> _lastTickerState = new();
        private const int TickerMinPushIntervalMs = 1000; // 1s 最小推送间隔

        // K线（当前未收线bar）去重缓存 key = symbol|interval|openTime
        private readonly ConcurrentDictionary<string, (string Hash, long LastPushMs)> _lastKLineState = new();
        private const int KLineMinPushIntervalMs = 1500; // 未收线K线最小推送间隔

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

            // 启动前预热 (Redis -> 内存) 订单簿快照，减少首次 books 推送前前端的空白期
            await PreloadOrderBookSnapshotsAsync(stoppingToken);

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
                            // 统一调用 SubscribeKLineAsync（内部现已直接使用 mark-price-candleX 频道）
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

        private async Task PreloadOrderBookSnapshotsAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var cache = scope.ServiceProvider.GetService<IOrderBookSnapshotCache>();
                if (cache == null) return;
                foreach (var symbol in _symbols)
                {
                    if (await cache.TryLoadAsync(symbol, ct))
                    {
                        var snap = cache.Get(symbol);
                        if (snap != null)
                        {
                            _logger.LogInformation("订单簿快照预热成功 {Symbol} ts={Ts}", symbol, snap.Value.timestamp);
                        }
                        else
                        {
                            _logger.LogInformation("订单簿快照预热成功但未取回 {Symbol}", symbol);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Redis 中无可预热订单簿快照 {Symbol}", symbol);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "预热订单簿快照失败");
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
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var hash = string.Concat(t.Last,'|',t.ChangePercent,'|',t.Volume24h,'|',t.High24h,'|',t.Low24h);
                var state = _lastTickerState.GetOrAdd(t.Symbol, _ => (0m,0m,0m,0m,0m,0L,string.Empty));
                // 若内容完全相同且在最小间隔内 -> 忽略
                if (state.Hash == hash && (nowMs - state.LastPushMs) < TickerMinPushIntervalMs)
                {
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var push = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
                var priceService = scope.ServiceProvider.GetService<IPriceDataService>();

                var priceData = new
                {
                    symbol = t.Symbol,
                    price = t.Last,
                    change24h = t.ChangePercent,
                    volume24h = t.Volume24h,
                    high24h = t.High24h,
                    low24h = t.Low24h,
                    timestamp = t.Ts
                };

                if (priceService != null)
                {
                    // 原逻辑: 复用当前 scope 的 priceService 并 ContinueWith -> scope 可能已在任务执行时被释放
                    // 新逻辑: fire-and-forget 任务内部创建独立作用域, 只捕获原始值, 不捕获 scoped 实例
                    var symbol = t.Symbol;
                    var last = t.Last;
                    var change = t.ChangePercent;
                    var vol = t.Volume24h;
                    var high = t.High24h;
                    var low = t.Low24h;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var persistScope = _scopeFactory.CreateScope();
                            var scopedPriceService = persistScope.ServiceProvider.GetRequiredService<IPriceDataService>();
                            await scopedPriceService.UpdateTradingPairPriceAsync(symbol, last, change, vol, high, low);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "价格持久化任务失败 {Symbol}", symbol);
                        }
                    }, CancellationToken.None);
                }

                await push.PushPriceDataAsync(t.Symbol, priceData);
                _lastTickerState[t.Symbol] = (t.Last, t.ChangePercent, t.Volume24h, t.High24h, t.Low24h, nowMs, hash);
                _logger.LogDebug("Ticker Relay 推送完成 {Symbol} price={Price} change={Change}", t.Symbol, t.Last, t.ChangePercent);
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
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var interval = k.Interval;
                var key = string.Concat(k.Symbol,'|',interval,'|',k.OpenTime);
                var hash = string.Concat(k.Open,'|',k.High,'|',k.Low,'|',k.Close,'|',k.Volume,'|',k.IsClosed);
                var state = _lastKLineState.GetOrAdd(key, _ => (string.Empty, 0L));

                // 未收线K线：内容未变且未达到节流窗口 -> 跳过
                if (!k.IsClosed && state.Hash == hash && (nowMs - state.LastPushMs) < KLineMinPushIntervalMs)
                {
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var push = scope.ServiceProvider.GetRequiredService<IRealTimeDataPushService>();
                var pairService = scope.ServiceProvider.GetService<IPriceDataService>();
                var klineService = scope.ServiceProvider.GetService<IKLineDataService>();
                int tradingPairId = await ResolveTradingPairIdAsync(k.Symbol, pairService, ct);

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

                if (tradingPairId > 0 && k.IsClosed && klineService != null)
                {
                    _logger.LogDebug("准备持久化K线 {Symbol} {Interval} open={Open} tpId={TpId}", k.Symbol, interval, k.OpenTime, tradingPairId);
                    var symbol = k.Symbol;
                    var intervalCopy = interval;
                    var entityCopy = new KLineData
                    {
                        TradingPairId = klineEntity.TradingPairId,
                        TimeFrame = klineEntity.TimeFrame,
                        OpenTime = klineEntity.OpenTime,
                        CloseTime = klineEntity.CloseTime,
                        Open = klineEntity.Open,
                        High = klineEntity.High,
                        Low = klineEntity.Low,
                        Close = klineEntity.Close,
                        Volume = klineEntity.Volume
                    };

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var persistScope = _scopeFactory.CreateScope();
                            var scopedKLineService = persistScope.ServiceProvider.GetRequiredService<IKLineDataService>();
                            await scopedKLineService.AddOrUpdateKLineDataAsync(entityCopy);
                            _logger.LogDebug("已持久化K线 {Symbol} {Interval} open={Open}", symbol, intervalCopy, entityCopy.OpenTime);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "K线持久化任务失败 {Symbol} {Interval} @ {Open}", symbol, intervalCopy, entityCopy.OpenTime);
                        }
                    }, CancellationToken.None);
                }

                await push.PushKLineDataAsync(k.Symbol, interval, klineEntity, k.IsClosed);
                _lastKLineState[key] = (hash, nowMs);
                _logger.LogDebug("KLine Relay 推送完成 {Symbol} {Interval} close={Close} closed={Closed}", k.Symbol, interval, k.Close, k.IsClosed);
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
