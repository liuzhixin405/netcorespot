using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.ExternalServices;

/// <summary>
/// OKX WebSocket 行情实现（公共频道 + business 频道 mark-price-candle）。
/// 连接、心跳、空闲检测与退避重连由 <see cref="ResilientWebSocketConnection"/> 统一处理。
/// </summary>
public class OkxMarketDataStreamProvider : IMarketDataStreamProvider, IAsyncDisposable
{
    private readonly ILogger<OkxMarketDataStreamProvider> _logger;
    private readonly IConfiguration _config;
    private readonly MarketDataMetrics _metrics;
    private readonly MarketDataFeedOptions _options;

    private readonly ResilientWebSocketConnection _public;
    private readonly ResilientWebSocketConnection _business;

    private readonly ConcurrentDictionary<string, byte> _subTickers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _subBooks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _subTrades = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Symbol, string Interval), byte> _subMarkPriceKLines = new();

    private CancellationTokenSource? _cts;
    private int _started;

    public OkxMarketDataStreamProvider(
        ILogger<OkxMarketDataStreamProvider> logger,
        IConfiguration config,
        MarketDataMetrics metrics,
        MarketDataFeedOptions options)
    {
        _logger = logger;
        _config = config;
        _metrics = metrics;
        _options = options;

        var publicUrl = _config["Okx:PublicWs"] ?? "wss://ws.okx.com:8443/ws/v5/public";
        var businessUrl = _config["Okx:BusinessWs"] ?? "wss://ws.okx.com:8443/ws/v5/business";

        _public = new ResilientWebSocketConnection(
            "okx-public",
            new Uri(publicUrl),
            OnPublicConnectedAsync,
            (message, ct) => HandleMessageAsync(_public!, message, business: false, ct),
            () => "ping",
            options,
            metrics,
            logger);

        _business = new ResilientWebSocketConnection(
            "okx-business",
            new Uri(businessUrl),
            OnBusinessConnectedAsync,
            (message, ct) => HandleMessageAsync(_business!, message, business: true, ct),
            () => "ping",
            options,
            metrics,
            logger);
    }

    public string ProviderName => "OKX";
    public bool HasStarted => Volatile.Read(ref _started) == 1;
    public bool IsConnected => _public.IsConnected;

    public DateTimeOffset? LastMessageAt
    {
        get
        {
            var a = _public.LastMessageAt;
            var b = _business.LastMessageAt;
            if (a is null) return b;
            if (b is null) return a;
            return a >= b ? a : b;
        }
    }

    public event Action<MarketTicker>? OnTicker;
    public event Action<OrderBookDelta>? OnOrderBook;
    public event Action<PublicTrade>? OnTrade;
    public event Action<KLineUpdate>? OnKLine;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _started, 1);
        _cts ??= CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await _public.StartAsync(cancellationToken);
        await _business.StartAsync(cancellationToken);

        // 等到公共连接就绪后再返回；business 端点允许延后重试。
        try
        {
            await _public.WaitUntilStartedAsync(cancellationToken)
                .WaitAsync(_options.ConnectTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("OKX 公共 WebSocket 在 {Timeout}s 内未就绪，将由后台重连", _options.ConnectTimeout.TotalSeconds);
        }

        try
        {
            await _business.WaitUntilStartedAsync(cancellationToken)
                .WaitAsync(_options.ConnectTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("OKX business WebSocket 在 {Timeout}s 内未就绪，将由后台重连", _options.ConnectTimeout.TotalSeconds);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();

        try
        {
            await Task.WhenAll(
                _public.StopAsync(cancellationToken),
                _business.StopAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task SubscribeBatchAsync(
        IReadOnlyList<string> symbols,
        int depth,
        string klineInterval = "1m",
        CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0)
        {
            return;
        }

        klineInterval = NormalizeInterval(klineInterval);
        var okxSymbols = symbols.Select(ToOkxSymbol).ToArray();

        foreach (var symbol in symbols)
        {
            _subTickers[symbol] = 1;
            _subBooks[symbol] = depth;
            _subTrades[symbol] = 1;
            _subMarkPriceKLines[(symbol, klineInterval)] = 1;
        }

        var bookChannel = depth <= 5 ? "books5" : "books";
        var publicArgs = new List<object>(symbols.Count * 3);
        foreach (var symbol in okxSymbols)
        {
            publicArgs.Add(new { channel = "tickers", instId = symbol });
            publicArgs.Add(new { channel = bookChannel, instId = symbol });
            publicArgs.Add(new { channel = "trades", instId = symbol });
        }

        if (_public.IsConnected)
        {
            await _public.SendTextAsync(
                JsonSerializer.Serialize(new { op = "subscribe", args = publicArgs }),
                cancellationToken);
        }

        var klineArgs = okxSymbols
            .Select(symbol => new { channel = $"mark-price-candle{klineInterval}", instId = symbol })
            .ToArray<object>();

        if (_business.IsConnected)
        {
            await _business.SendTextAsync(
                JsonSerializer.Serialize(new { op = "subscribe", args = klineArgs }),
                cancellationToken);
        }
    }

    public async Task UnsubscribeBatchAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0)
        {
            return;
        }

        var okxSymbols = symbols.Select(ToOkxSymbol).ToArray();
        var publicArgs = new List<object>(symbols.Count * 3);

        var klineKeys = symbols
            .SelectMany(symbol => _subMarkPriceKLines.Keys.Where(key => key.Symbol == symbol))
            .ToArray();

        var bookChannels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            bookChannels[symbol] = _subBooks.TryGetValue(symbol, out var depth) && depth > 5 ? "books" : "books5";
            _subTickers.TryRemove(symbol, out _);
            _subBooks.TryRemove(symbol, out _);
            _subTrades.TryRemove(symbol, out _);

            foreach (var key in _subMarkPriceKLines.Keys.Where(key => key.Symbol == symbol).ToArray())
            {
                _subMarkPriceKLines.TryRemove(key, out _);
            }
        }

        foreach (var symbol in symbols)
        {
            var instId = ToOkxSymbol(symbol);
            publicArgs.Add(new { channel = "tickers", instId });
            publicArgs.Add(new { channel = bookChannels[symbol], instId });
            publicArgs.Add(new { channel = "trades", instId });
        }

        if (_public.IsConnected)
        {
            await _public.SendTextAsync(
                JsonSerializer.Serialize(new { op = "unsubscribe", args = publicArgs }),
                cancellationToken);
        }

        var klineArgs = klineKeys
            .Select(key => new { channel = $"mark-price-candle{key.Interval}", instId = ToOkxSymbol(key.Symbol) })
            .ToArray<object>();

        if (_business.IsConnected)
        {
            await _business.SendTextAsync(
                JsonSerializer.Serialize(new { op = "unsubscribe", args = klineArgs }),
                cancellationToken);
        }
    }

    public Task SubscribeTickerAsync(string symbol, CancellationToken cancellationToken = default)
        => SubscribeBatchAsync(new[] { symbol }, 5, "1m", cancellationToken);

    public Task SubscribeOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken = default)
        => SubscribeBatchAsync(new[] { symbol }, depth, "1m", cancellationToken);

    public Task SubscribeTradesAsync(string symbol, CancellationToken cancellationToken = default)
        => SubscribeBatchAsync(new[] { symbol }, 5, "1m", cancellationToken);

    public async Task SubscribeKLineAsync(string symbol, string interval, CancellationToken cancellationToken = default)
    {
        interval = NormalizeInterval(interval);
        _subMarkPriceKLines[(symbol, interval)] = 1;

        if (_business.IsConnected)
        {
            await _business.SendTextAsync(
                JsonSerializer.Serialize(new
                {
                    op = "subscribe",
                    args = new[] { new { channel = $"mark-price-candle{interval}", instId = ToOkxSymbol(symbol) } }
                }),
                cancellationToken);
        }
    }

    private async Task OnPublicConnectedAsync(ResilientWebSocketConnection connection, CancellationToken ct)
    {
        var symbols = _subTickers.Keys.Union(_subBooks.Keys).Union(_subTrades.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (symbols.Length == 0)
        {
            return;
        }

        var args = new List<object>(symbols.Length * 3);
        foreach (var symbol in symbols)
        {
            var instId = ToOkxSymbol(symbol);
            var bookDepth = _subBooks.TryGetValue(symbol, out var depth) ? depth : 5;
            args.Add(new { channel = "tickers", instId });
            args.Add(new { channel = bookDepth <= 5 ? "books5" : "books", instId });
            args.Add(new { channel = "trades", instId });
        }

        await connection.SendTextAsync(
            JsonSerializer.Serialize(new { op = "subscribe", args }),
            ct);
    }

    private async Task OnBusinessConnectedAsync(ResilientWebSocketConnection connection, CancellationToken ct)
    {
        var keys = _subMarkPriceKLines.Keys.ToArray();
        if (keys.Length == 0)
        {
            return;
        }

        var args = keys
            .Select(key => new { channel = $"mark-price-candle{key.Interval}", instId = ToOkxSymbol(key.Symbol) })
            .ToArray<object>();

        await connection.SendTextAsync(
            JsonSerializer.Serialize(new { op = "subscribe", args }),
            ct);
    }

    private async Task HandleMessageAsync(
        ResilientWebSocketConnection connection,
        ReadOnlyMemory<byte> message,
        bool business,
        CancellationToken ct)
    {
        // OKX 对应用层心跳的响应是纯文本 "pong"（不是 JSON）；必须先于 JSON 解析处理。
        var payload = message.Span;

        if (payload.SequenceEqual("pong"u8) || payload.SequenceEqual("PONG"u8))
        {
            return;
        }

        if (payload.SequenceEqual("ping"u8) || payload.SequenceEqual("PING"u8))
        {
            await connection.SendTextAsync("pong", ct);
            return;
        }

        // [联网-官方: https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsondocument.parse]
        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;

        if (root.TryGetProperty("event", out var evtEl))
        {
            var evt = evtEl.GetString();

            if (evt == "pong" || evt == "subscribe" || evt == "login")
            {
                return;
            }

            if (evt == "ping")
            {
                await connection.SendTextAsync("pong", ct);
                return;
            }

            if (evt == "error")
            {
                var code = root.TryGetProperty("code", out var cEl) ? cEl.GetString() : "";
                var msg = root.TryGetProperty("msg", out var mEl) ? mEl.GetString() : "";
                string? errorChannel = null;
                string? errorInstId = null;

                if (root.TryGetProperty("arg", out var aEl) && aEl.ValueKind == JsonValueKind.Object)
                {
                    errorChannel = aEl.TryGetProperty("channel", out var chEl) ? chEl.GetString() : null;
                    errorInstId = aEl.TryGetProperty("instId", out var idEl) ? idEl.GetString() : null;
                }

                _logger.LogWarning(
                    "OKX 订阅错误 code={Code} msg={Msg} channel={Channel} instId={Inst} business={Business}",
                    code, msg, errorChannel, errorInstId, business);

                return;
            }

            return;
        }

        if (!root.TryGetProperty("arg", out var arg))
        {
            return;
        }

        var channel = arg.GetProperty("channel").GetString();
        var instId = arg.GetProperty("instId").GetString() ?? string.Empty;
        var symbol = instId.Replace("-", string.Empty);

        if (!root.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        switch (channel)
        {
            case "tickers":
                foreach (var d in dataArr.EnumerateArray())
                {
                    var last = GetDecimal(d, "last");
                    var high = GetDecimal(d, "high24h");
                    var low = GetDecimal(d, "low24h");
                    var volume24h = GetDecimal(d, "vol24h");
                    var open24h = GetDecimal(d, "open24h");
                    var changePercent = open24h > 0 ? (last - open24h) / open24h : 0m;

                    var ticker = new MarketTicker(symbol, last, high, low, volume24h, changePercent, GetLong(d, "ts"));
                    OnTicker?.Invoke(ticker);
                }
                break;

            case "books5":
            case "books":
                var bookDepth = _subBooks.TryGetValue(symbol, out var requestedDepth) ? requestedDepth : 5;
                var maxBookLevels = channel == "books" ? int.MaxValue : bookDepth;
                var isSnapshot = !root.TryGetProperty("action", out var action) ||
                    action.ValueKind != JsonValueKind.String ||
                    action.GetString() != "update";
                foreach (var d in dataArr.EnumerateArray())
                {
                    var bids = ParseLevels(d, "bids", maxBookLevels);
                    var asks = ParseLevels(d, "asks", maxBookLevels);
                    var delta = new OrderBookDelta(symbol, isSnapshot, bids, asks, GetLong(d, "ts"));
                    OnOrderBook?.Invoke(delta);
                }
                break;

            case "trades":
                foreach (var d in dataArr.EnumerateArray())
                {
                    var trade = new PublicTrade(
                        symbol,
                        ParseLong(d.GetProperty("tradeId").GetString()),
                        GetDecimal(d, "px"),
                        GetDecimal(d, "sz"),
                        d.GetProperty("side").GetString() ?? string.Empty,
                        GetLong(d, "ts"));

                    OnTrade?.Invoke(trade);
                }
                break;

            default:
                if (channel is not null && channel.StartsWith("mark-price-candle", StringComparison.Ordinal))
                {
                    var interval = NormalizeInterval(channel["mark-price-candle".Length..]);
                    foreach (var d in dataArr.EnumerateArray())
                    {
                        if (d.ValueKind != JsonValueKind.Array || d.GetArrayLength() < 6)
                        {
                            continue;
                        }

                        var openTime = ParseLong(d[0].GetString());
                        var open = ParseDecimal(d[1].GetString());
                        var high = ParseDecimal(d[2].GetString());
                        var low = ParseDecimal(d[3].GetString());
                        var close = ParseDecimal(d[4].GetString());
                        var confirm = d[5].GetString() == "1";
                        var closeTime = openTime + GetIntervalMilliseconds(interval) - 1;

                        OnKLine?.Invoke(new KLineUpdate(
                            symbol, interval, openTime, closeTime, open, high, low, close, 0m, confirm,
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                    }
                }
                break;
        }
    }

    private static IReadOnlyList<OrderBookLevel> ParseLevels(JsonElement d, string name, int maxLevels)
    {
        if (!d.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<OrderBookLevel>();
        }

        var capacity = maxLevels == int.MaxValue ? arr.GetArrayLength() : Math.Min(arr.GetArrayLength(), maxLevels);
        var list = new List<OrderBookLevel>(capacity);
        foreach (var level in arr.EnumerateArray())
        {
            if (list.Count >= maxLevels)
            {
                break;
            }

            if (level.ValueKind == JsonValueKind.Array && level.GetArrayLength() >= 2)
            {
                list.Add(new OrderBookLevel
                {
                    Price = ParseDecimal(level[0].GetString()),
                    Quantity = ParseDecimal(level[1].GetString())
                });
            }
        }

        return list;
    }

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0L;

    private static decimal GetDecimal(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.GetString() is string s
            ? ParseDecimal(s)
            : 0m;

    private static long GetLong(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.GetString() is string s
            ? ParseLong(s)
            : 0L;

    private static string ToOkxSymbol(string symbol)
    {
        if (symbol.Contains('-'))
        {
            return symbol;
        }

        if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
        {
            return symbol[..^4] + "-USDT";
        }

        return symbol;
    }

    private static string NormalizeInterval(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var value = raw.Trim();
        if (value.Length < 2)
        {
            return value;
        }

        var digits = value[..^1];
        var unit = char.ToLowerInvariant(value[^1]);
        return unit switch
        {
            'm' => digits + "m",
            'h' => digits + "H",
            'd' => digits + "D",
            'w' => digits + "W",
            'y' => digits + "Y",
            _ => value
        };
    }

    private static long GetIntervalMilliseconds(string interval)
    {
        if (interval.Length < 2 || !long.TryParse(interval[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return 60_000L;
        }

        return char.ToLowerInvariant(interval[^1]) switch
        {
            'm' => number * 60_000L,
            'h' => number * 3_600_000L,
            'd' => number * 86_400_000L,
            'w' => number * 604_800_000L,
            'y' => number * 31_536_000_000L,
            _ => 60_000L
        };
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts?.Dispose();
    }
}
