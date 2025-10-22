using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Domain.Entities; // 为 OrderBookLevel

namespace CryptoSpot.Infrastructure.ExternalServices
{
    /// <summary>
    /// OKX WebSocket 行情实现 (公共频道 + business 频道 mark-price-candle)
    /// 仅做最小 MVP：Ticker / OrderBook (books5) / Trades / 1m KLine
    /// 后续可扩展：books50-l2-tbt 增量 + Checksum 验证 + 断线补偿
    /// </summary>
    public class OkxMarketDataStreamProvider : IMarketDataStreamProvider, IAsyncDisposable
    {
        private readonly ILogger<OkxMarketDataStreamProvider> _logger;
        private readonly IConfiguration _config;
        private ClientWebSocket? _ws;              // public
        private ClientWebSocket? _businessWs;      // business (mark-price-candle)
        private readonly Uri _publicUrl;
        private readonly Uri _businessUrl;
        private readonly TimeSpan _pingInterval = TimeSpan.FromSeconds(25);
        private readonly ConcurrentDictionary<string, bool> _subTickers = new();
        private readonly ConcurrentDictionary<string, bool> _subBooks = new();
        private readonly ConcurrentDictionary<string, bool> _subTrades = new();
        private readonly ConcurrentDictionary<(string Symbol, string Interval), bool> _subKLines = new();
        private readonly ConcurrentDictionary<(string Symbol, string Interval), bool> _subMarkPriceKLines = new(); // 新增 mark-price 订阅集合
        private readonly ConcurrentDictionary<(string Symbol, string Interval), bool> _klineFallbackTried = new(); // 记录已针对 candleX 失败并回退的标记，避免死循环
        private CancellationTokenSource? _cts;
        private Task? _recvLoop;
        private Task? _pingLoop;
        private Task? _recvBusinessLoop;
        private Task? _pingBusinessLoop;

        public string ProviderName => "OKX";
        public bool IsConnected => (_ws?.State == WebSocketState.Open) || (_businessWs?.State == WebSocketState.Open);

        public event Action<MarketTicker>? OnTicker;
        public event Action<OrderBookDelta>? OnOrderBook;
        public event Action<PublicTrade>? OnTrade;
        public event Action<KLineUpdate>? OnKLine;

        public OkxMarketDataStreamProvider(ILogger<OkxMarketDataStreamProvider> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
            var url = _config["Okx:PublicWs"] ?? "wss://ws.okx.com:8443/ws/v5/public";
            var biz = _config["Okx:BusinessWs"] ?? "wss://ws.okx.com:8443/ws/v5/business";
            _publicUrl = new Uri(url);
            _businessUrl = new Uri(biz);
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsConnected) return;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // public ws
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(_publicUrl, cancellationToken);
            _logger.LogInformation("OKX Public WS 已连接: {Url}", _publicUrl);
            _recvLoop = Task.Run(() => ReceiveLoop(_ws, false, _cts.Token));
            _pingLoop = Task.Run(() => PingLoop(_ws, _cts.Token));

            // business ws (专门用于 mark-price-candle，避免与公共频道互相影响)
            _businessWs = new ClientWebSocket();
            try
            {
                await _businessWs.ConnectAsync(_businessUrl, cancellationToken);
                _logger.LogInformation("OKX Business WS 已连接: {Url}", _businessUrl);
                _recvBusinessLoop = Task.Run(() => ReceiveLoop(_businessWs, true, _cts.Token));
                _pingBusinessLoop = Task.Run(() => PingLoop(_businessWs, _cts.Token));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "业务端点连接失败(可延后重试) {Url}", _businessUrl);
            }

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
                if (_businessWs != null && (_businessWs.State == WebSocketState.Open || _businessWs.State == WebSocketState.CloseReceived))
                {
                    await _businessWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "client disconnect", cancellationToken);
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
            foreach (var kv in _subMarkPriceKLines.Keys) await SubscribeMarkPriceKLineAsync(kv.Symbol, kv.Interval, ct);
        }

        private static string NormalizeInterval(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var v = raw.Trim();
            var match = Regex.Match(v, "^(\\d+)([a-zA-Z])$");
            if (!match.Success) return v;
            var num = match.Groups[1].Value;
            var unit = match.Groups[2].Value.ToLower();
            return unit switch
            {
                "m" => num + "m",
                "h" => num + "H",
                "d" => num + "D",
                "w" => num + "W",
                "y" => num + "Y",
                _ => v
            };
        }

        public async Task SubscribeTickerAsync(string symbol, CancellationToken cancellationToken = default)
        {
            _subTickers[symbol] = true;
            await SendPublicAsync(new { op = "subscribe", args = new[] { new { channel = "tickers", instId = ToOkxSymbol(symbol) } } }, cancellationToken);
        }

        public async Task SubscribeOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken = default)
        {
            _subBooks[symbol] = true;
            var channel = depth <= 5 ? "books5" : "books";
            await SendPublicAsync(new { op = "subscribe", args = new[] { new { channel, instId = ToOkxSymbol(symbol) } } }, cancellationToken);
        }

        public async Task SubscribeTradesAsync(string symbol, CancellationToken cancellationToken = default)
        {
            _subTrades[symbol] = true;
            await SendPublicAsync(new { op = "subscribe", args = new[] { new { channel = "trades", instId = ToOkxSymbol(symbol) } } }, cancellationToken);
        }

        public async Task SubscribeKLineAsync(string symbol, string interval, CancellationToken cancellationToken = default)
        {
            // 直接采用 mark-price-candleX (business WS) 避免 60012 错误
            interval = NormalizeInterval(interval);
            _subMarkPriceKLines[(symbol, interval)] = true; // 使用 mark-price 记录
            var ch = $"mark-price-candle{interval}";
            await SendBusinessAsync(new { op = "subscribe", args = new[] { new { channel = ch, instId = ToOkxSymbol(symbol) } } }, cancellationToken);
            _logger.LogInformation("OKX 直接订阅 mark-price-kline {Symbol} {Interval}", symbol, interval);
        }

        private async Task SubscribeMarkPriceKLineAsync(string symbol, string interval, CancellationToken ct)
        {
            interval = NormalizeInterval(interval);
            _subMarkPriceKLines[(symbol, interval)] = true;
            var ch = $"mark-price-candle{interval}";
            await SendBusinessAsync(new { op = "subscribe", args = new[] { new { channel = ch, instId = ToOkxSymbol(symbol) } } }, ct);
        }

        private string ToOkxSymbol(string symbol)
        {
            if (symbol.Contains('-')) return symbol;
            if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            {
                var baseAsset = symbol.Substring(0, symbol.Length - 4);
                return baseAsset + "-USDT";
            }
            return symbol;
        }

        private async Task SendPublicAsync(object payload, CancellationToken ct) => await InternalSendAsync(_ws, payload, ct, false);
        private async Task SendBusinessAsync(object payload, CancellationToken ct) => await InternalSendAsync(_businessWs, payload, ct, true);

        private async Task InternalSendAsync(ClientWebSocket? socket, object payload, CancellationToken ct, bool business)
        {
            if (socket == null || socket.State != WebSocketState.Open) return;
            var json = JsonSerializer.Serialize(payload);
            if (!json.TrimEnd().EndsWith("}"))
            {
                _logger.LogWarning("发送JSON格式异常(缺少结束大括号?) business={Business}: {Json}", business, json);
            }
            var buf = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(buf, WebSocketMessageType.Text, true, ct);
            _logger.LogDebug("--> OKX {Type}: {Json}", business ? "BUS" : "PUB", json);
        }

        private async Task SendAsync(object payload, CancellationToken ct) => await SendPublicAsync(payload, ct);

        private async Task ReceiveLoop(ClientWebSocket? socket, bool business, CancellationToken ct)
        {
            if (socket == null) return;
            var buffer = new byte[64 * 1024];
            while (!ct.IsCancellationRequested && socket != null)
            {
                try
                {
                    var ms = new MemoryStream();
                    WebSocketReceiveResult? result;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogWarning("OKX WS({Type}) 收到关闭帧: {Status}", business ? "BUS" : "PUB", result.CloseStatus);
                            await ConnectRetryAsync(ct);
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    _logger.LogTrace("<-- OKX {Type}: {Text}", business ? "BUS" : "PUB", text);
                    HandleMessage(text);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OKX 接收循环异常({Type})，准备重连", business ? "BUS" : "PUB");
                    await ConnectRetryAsync(ct);
                    return;
                }
            }
        }

        private async Task ConnectRetryAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                await ConnectAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重连失败，将继续重试");
            }
        }        private async Task PingLoop(ClientWebSocket? socket, CancellationToken ct)
        {
            // OKX WebSocket API 不支持自定义 ping 消息格式 {"op":"ping"}
            // 依赖 ClientWebSocket 内置的心跳机制即可
            // 这个方法现在只是保持活跃的占位符，不发送任何消息
            while (!ct.IsCancellationRequested && socket != null && socket.State == WebSocketState.Open)
            {
                try
                {
                    await Task.Delay(_pingInterval, ct);
                    _logger.LogTrace("OKX WebSocket 连接检查 - 状态: {State}", socket.State);
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
                    if (evt == "error")
                    {
                        var code = root.TryGetProperty("code", out var cEl) ? cEl.GetString() : "";
                        var msg = root.TryGetProperty("msg", out var mEl) ? mEl.GetString() : "";
                        string? channel = null; string? instId = null;
                        if (root.TryGetProperty("arg", out var aEl) && aEl.ValueKind == JsonValueKind.Object)
                        {
                            channel = aEl.TryGetProperty("channel", out var chEl) ? chEl.GetString() : null;
                            instId = aEl.TryGetProperty("instId", out var idEl) ? idEl.GetString() : null;
                        }
                        _logger.LogWarning("OKX 订阅错误 code={Code} msg={Msg} channel={Channel} instId={Inst}", code, msg, channel, instId);
                        if (code == "60012" && channel != null && channel.StartsWith("candle") && instId != null)
                        {
                            var symbol = instId.Replace("-", string.Empty);
                            var interval = channel.Replace("candle", string.Empty);
                            if (!_klineFallbackTried.ContainsKey((symbol, interval)))
                            {
                                _klineFallbackTried[(symbol, interval)] = true;
                                _logger.LogInformation("尝试回退订阅 mark-price-candle{Interval} {Symbol}", interval, symbol);
                                _ = Task.Run(async () =>
                                {
                                    try { await SubscribeMarkPriceKLineAsync(symbol, interval, CancellationToken.None); } catch (Exception ex) { _logger.LogError(ex, "回退订阅失败 {Symbol} {Interval}", symbol, interval); }
                                });
                            }
                        }
                        return;
                    }
                    if (evt == "subscribe" || evt == "pong" || evt == "login") return;
                }
                if (root.TryGetProperty("arg", out var arg))
                {
                    var channel = arg.GetProperty("channel").GetString();
                    var instId = arg.GetProperty("instId").GetString() ?? string.Empty;
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
                                    // 使用 vol24h（基础货币交易量）而不是 volCcy24h（计价货币交易量）
                                    var volume24h = GetDecimal(d, "vol24h");
                                    var open24h = GetDecimal(d, "open24h");
                                    decimal changePercent = 0m;
                                    if (open24h > 0)
                                    {
                                        changePercent = (last - open24h) / open24h;
                                    }
                                    var ticker = new MarketTicker(
                                        Symbol: symbol,
                                        Last: last,
                                        High24h: high,
                                        Low24h: low,
                                        Volume24h: volume24h,
                                        ChangePercent: changePercent,
                                        Ts: GetLong(d, "ts"));
                                    
                                    _logger.LogDebug("📊 OKX Ticker: {Symbol} Price={Last} Change={Change:P2} Vol={Vol}", 
                                        symbol, last, changePercent, volume24h);
                                    
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
                                    var delta = new OrderBookDelta(symbol, true, bids, asks, ts);
                                    OnOrderBook?.Invoke(delta);
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
                            default:
                                if (channel != null && (channel.StartsWith("candle") || channel.StartsWith("mark-price-candle")))
                                {
                                    foreach (var d in dataArr.EnumerateArray())
                                    {
                                        if (d.ValueKind != JsonValueKind.Array) continue;

                                        if (channel.StartsWith("mark-price-candle"))
                                        {
                                            // mark-price-candleX: [ ts, open, high, low, close, confirmFlag ]
                                            if (d.GetArrayLength() >= 6)
                                            {
                                                var ts = long.Parse(d[0].GetString()!);
                                                var open = decimal.Parse(d[1].GetString()!);
                                                var high = decimal.Parse(d[2].GetString()!);
                                                var low = decimal.Parse(d[3].GetString()!);
                                                var close = decimal.Parse(d[4].GetString()!);
                                                var confirm = d[5].GetString() == "1"; // 1=收线
                                                var interval = channel.Replace("mark-price-candle", string.Empty);
                                                var kline = new KLineUpdate(symbol, interval, ts, open, high, low, close, 0m, confirm, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                                                OnKLine?.Invoke(kline);
                                            }
                                        }
                                        else
                                        {
                                            // 普通 candleX: [ ts, o, h, l, c, vol, volCcy/?, volCcyQuote?, confirmFlag, ...] 至少取前8位与第8位confirm
                                            if (d.GetArrayLength() >= 8)
                                            {
                                                var ts = long.Parse(d[0].GetString()!);
                                                var open = decimal.Parse(d[1].GetString()!);
                                                var high = decimal.Parse(d[2].GetString()!);
                                                var low = decimal.Parse(d[3].GetString()!);
                                                var close = decimal.Parse(d[4].GetString()!);
                                                var vol = decimal.Parse(d[5].GetString()!);
                                                var confirm = d[7].GetString() == "1"; // 第8位为是否收线
                                                var interval = channel.Replace("candle", string.Empty);
                                                var kline = new KLineUpdate(symbol, interval, ts, open, high, low, close, vol, confirm, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                                                OnKLine?.Invoke(kline);
                                            }
                                        }
                                    }
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
            _businessWs?.Dispose();
            _cts?.Dispose();
        }
    }
}
