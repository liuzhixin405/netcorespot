using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces.Trading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CryptoSpot.Infrastructure.ExternalServices
{
    /// <summary>
    /// OKX WebSocket 行情实现 (公共频道)
    /// 仅做最小 MVP：Ticker / OrderBook (books5) / Trades / 1m KLine
    /// 后续可扩展：books50-l2-tbt 增量 + Checksum 验证 + 断线补偿
    /// </summary>
    public class OkxMarketDataStreamProvider : IMarketDataStreamProvider, IAsyncDisposable
    {
        private readonly ILogger<OkxMarketDataStreamProvider> _logger;
        private readonly IConfiguration _config;
        private ClientWebSocket? _ws;
        private readonly Uri _publicUrl;
        private readonly TimeSpan _pingInterval = TimeSpan.FromSeconds(25);
        private readonly ConcurrentDictionary<string, bool> _subTickers = new();
        private readonly ConcurrentDictionary<string, bool> _subBooks = new();
        private readonly ConcurrentDictionary<string, bool> _subTrades = new();
        private readonly ConcurrentDictionary<(string Symbol,string Interval), bool> _subKLines = new();
        private CancellationTokenSource? _cts;
        private Task? _recvLoop;
        private Task? _pingLoop;

        public string ProviderName => "OKX";
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public event Action<MarketTicker>? OnTicker;
        public event Action<OrderBookDelta>? OnOrderBook;
        public event Action<PublicTrade>? OnTrade;
        public event Action<KLineUpdate>? OnKLine;

        public OkxMarketDataStreamProvider(ILogger<OkxMarketDataStreamProvider> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
            var url = _config["Okx:PublicWs"] ?? "wss://ws.okx.com:8443/ws/v5/public";
            _publicUrl = new Uri(url);
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsConnected) return;
            _ws = new ClientWebSocket();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await _ws.ConnectAsync(_publicUrl, cancellationToken);
            _logger.LogInformation("OKX WS 已连接: {Url}", _publicUrl);
            _recvLoop = Task.Run(() => ReceiveLoop(_cts.Token));
            _pingLoop = Task.Run(() => PingLoop(_cts.Token));
            await ResubscribeAllAsync(cancellationToken);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _cts?.Cancel();
                if (_ws != null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived))
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client disconnect", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "关闭 OKX WS 时异常");
            }
        }

        private async Task ResubscribeAllAsync(CancellationToken ct)
        {
            foreach (var s in _subTickers.Keys) await SubscribeTickerAsync(s, ct);
            foreach (var s in _subTrades.Keys) await SubscribeTradesAsync(s, ct);
            foreach (var s in _subBooks.Keys) await SubscribeOrderBookAsync(s, 5, ct);
            foreach (var kv in _subKLines.Keys) await SubscribeKLineAsync(kv.Symbol, kv.Interval, ct);
        }

        public async Task SubscribeTickerAsync(string symbol, CancellationToken cancellationToken = default)
        {
            _subTickers[symbol] = true;
            await SendAsync(new { op = "subscribe", args = new[] { new { channel = "tickers", instId = ToOkxSymbol(symbol) } } }, cancellationToken);
        }

        public async Task SubscribeOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken = default)
        {
            // 使用 books5 作为初版
            _subBooks[symbol] = true;
            var channel = depth <= 5 ? "books5" : "books"; // 后续扩展 books50-l2-tbt
            await SendAsync(new { op = "subscribe", args = new[] { new { channel, instId = ToOkxSymbol(symbol) } } }, cancellationToken);
        }

        public async Task SubscribeTradesAsync(string symbol, CancellationToken cancellationToken = default)
        {
            _subTrades[symbol] = true;
            await SendAsync(new { op = "subscribe", args = new[] { new { channel = "trades", instId = ToOkxSymbol(symbol) } } }, cancellationToken);
        }

        public async Task SubscribeKLineAsync(string symbol, string interval, CancellationToken cancellationToken = default)
        {
            _subKLines[(symbol, interval)] = true;
            var ch = $"candle{interval}"; // interval: 1m 5m 15m 1H ... 统一传入
            await SendAsync(new { op = "subscribe", args = new[] { new { channel = ch, instId = ToOkxSymbol(symbol) } } }, cancellationToken);
        }

        private string ToOkxSymbol(string symbol)
        {
            // 内部用 BTCUSDT, OKX 用 BTC-USDT
            if (symbol.Contains('-')) return symbol;
            if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            {
                var baseAsset = symbol.Substring(0, symbol.Length - 4);
                return baseAsset + "-USDT";
            }
            return symbol;
        }

        private async Task SendAsync(object payload, CancellationToken ct)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            var json = JsonSerializer.Serialize(payload);
            var buf = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(buf, WebSocketMessageType.Text, true, ct);
            _logger.LogDebug("--> OKX: {Json}", json);
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            while (!ct.IsCancellationRequested && _ws != null)
            {
                try
                {
                    var ms = new MemoryStream();
                    WebSocketReceiveResult? result;
                    do
                    {
                        result = await _ws.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogWarning("OKX WS 收到关闭帧: {Status}", result.CloseStatus);
                            await ConnectRetryAsync(ct); // 自动重连
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    _logger.LogTrace("<-- OKX: {Text}", text);
                    HandleMessage(text);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OKX 接收循环异常，准备重连");
                    await ConnectRetryAsync(ct);
                    return;
                }
            }
        }

        private async Task ConnectRetryAsync(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            await ConnectAsync(ct);
        }

        private async Task PingLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _ws != null)
            {
                try
                {
                    await Task.Delay(_pingInterval, ct);
                    await SendAsync(new { op = "ping" }, ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OKX PingLoop 异常，忽略");
                }
            }
        }

        private void HandleMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("event", out var evtEl))
                {
                    var evt = evtEl.GetString();
                    if (evt == "subscribe" || evt == "pong" || evt == "login") return; // 忽略
                }
                if (root.TryGetProperty("arg", out var arg))
                {
                    var channel = arg.GetProperty("channel").GetString();
                    var instId = arg.GetProperty("instId").GetString() ?? string.Empty; // BTC-USDT
                    var symbol = instId.Replace("-", string.Empty);
                    if (root.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                    {
                        switch (channel)
                        {
                            case "tickers":
                                foreach (var d in dataArr.EnumerateArray())
                                {
                                    var last = GetDecimal(d, "last");
                                    var high = GetDecimal(d, "high24h");
                                    var low = GetDecimal(d, "low24h");
                                    var volQuote = GetDecimal(d, "volCcy24h");
                                    var open24h = GetDecimal(d, "open24h");
                                    decimal changePercent = 0m;
                                    if (open24h > 0)
                                    {
                                        changePercent = (last - open24h) / open24h; // 规范化为小数 (0.0123 = +1.23%)
                                    }
                                    var ticker = new MarketTicker(symbol,
                                        last,
                                        high,
                                        low,
                                        volQuote,
                                        changePercent,
                                        GetLong(d, "ts"));
                                    OnTicker?.Invoke(ticker);
                                }
                                break;
                            case "books5":
                            case "books":
                                foreach (var d in dataArr.EnumerateArray())
                                {
                                    var bids = ParseLevels(d, "bids");
                                    var asks = ParseLevels(d, "asks");
                                    var ts = GetLong(d, "ts");
                                    var delta = new OrderBookDelta(symbol, true, bids, asks, ts); // OKX books5 推送就是快照形式
                                    OnOrderBook?.Invoke(delta);
                                }
                                break;
                            case var s when s != null && s.StartsWith("candle"):
                                foreach (var d in dataArr.EnumerateArray())
                                {
                                    // d 是数组: [ts, o,h,l,c,vol,volCcy,confirm]
                                    if (d.ValueKind == JsonValueKind.Array && d.GetArrayLength() >= 8)
                                    {
                                        var ts = long.Parse(d[0].GetString()!);
                                        var open = decimal.Parse(d[1].GetString()!);
                                        var high = decimal.Parse(d[2].GetString()!);
                                        var low = decimal.Parse(d[3].GetString()!);
                                        var close = decimal.Parse(d[4].GetString()!);
                                        var vol = decimal.Parse(d[5].GetString()!);
                                        var confirm = d[7].GetString() == "1"; // 1=收线
                                        var interval = channel!.Replace("candle", string.Empty);
                                        var kline = new KLineUpdate(symbol, interval, ts, open, high, low, close, vol, confirm, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                                        OnKLine?.Invoke(kline);
                                    }
                                }
                                break;
                            case "trades":
                                foreach (var d in dataArr.EnumerateArray())
                                {
                                    var trade = new PublicTrade(symbol,
                                        long.Parse(d.GetProperty("tradeId").GetString() ?? "0"),
                                        GetDecimal(d, "px"),
                                        GetDecimal(d, "sz"),
                                        d.GetProperty("side").GetString() ?? "", GetLong(d, "ts"));
                                    OnTrade?.Invoke(trade);
                                }
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析 OKX 消息失败: {Json}", json);
            }
        }

        private IReadOnlyList<OrderBookLevel> ParseLevels(JsonElement d, string name)
        {
            var list = new List<OrderBookLevel>();
            if (d.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var level in arr.EnumerateArray())
                {
                    if (level.ValueKind == JsonValueKind.Array && level.GetArrayLength() >= 2)
                    {
                        var price = decimal.Parse(level[0].GetString()!);
                        var size = decimal.Parse(level[1].GetString()!);
                        list.Add(new OrderBookLevel { Price = price, Quantity = size });
                    }
                }
            }
            return list;
        }

        private static decimal GetDecimal(JsonElement el, string prop)
        {
            return el.TryGetProperty(prop, out var v) && v.GetString() is string s && decimal.TryParse(s, out var d) ? d : 0m;
        }
        private static long GetLong(JsonElement el, string prop)
        {
            return el.TryGetProperty(prop, out var v) && v.GetString() is string s && long.TryParse(s, out var l) ? l : 0L;
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
            _ws?.Dispose();
            _cts?.Dispose();
        }
    }
}
