using System.Collections.Concurrent;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.BackgroundServices;
using CryptoSpot.Infrastructure.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CryptoSpot.Infrastructure.ExternalServices;

/// <summary>
/// Binance 市场数据提供者（REST 轮询，作为可选备用源）。默认不启用；
/// 主实时源仍是 OKX WebSocket。价格落库统一交给 <see cref="PriceUpdateBatchService"/>。
/// </summary>
public class BinanceMarketDataProvider : IMarketDataProvider
{
    private static readonly string[] DefaultSymbols = LocalMarketCatalog.Symbols;
    private static readonly string[] DefaultIntervals = { "1m", "5m", "15m", "1h", "4h", "1d" };

    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IRealTimeDataPushService _realTimeDataPush;
    private readonly PriceUpdateBatchService _priceUpdateBatchService;
    private readonly ILogger<BinanceMarketDataProvider> _logger;
    private readonly IConfiguration _configuration;
    private readonly MarketDataMetrics _metrics;
    private readonly Timer _timer;

    private readonly ConcurrentDictionary<string, KLineDataDto> _klineCache = new();
    private readonly ConcurrentDictionary<string, long> _tradingPairIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _symbols;
    private readonly string[] _intervals;
    private readonly int _maxConcurrency;
    private readonly string _proxyUrl;

    private CancellationTokenSource? _cts;
    private DateTime _lastKlineSaveMinute = DateTime.MinValue;
    private int _isSyncing;

    public string ProviderName => "Binance";

    public BinanceMarketDataProvider(
        HttpClient httpClient,
        IServiceScopeFactory serviceScopeFactory,
        IRealTimeDataPushService realTimeDataPush,
        PriceUpdateBatchService priceUpdateBatchService,
        MarketDataMetrics metrics,
        ILogger<BinanceMarketDataProvider> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _serviceScopeFactory = serviceScopeFactory;
        _realTimeDataPush = realTimeDataPush;
        _priceUpdateBatchService = priceUpdateBatchService;
        _metrics = metrics;
        _logger = logger;
        _configuration = configuration;

        _symbols = ReadList("Binance:Symbols", DefaultSymbols);
        _intervals = ReadList("Binance:Intervals", DefaultIntervals);
        _maxConcurrency = Math.Clamp(configuration.GetValue("Binance:MaxConcurrency", 4), 1, 16);
        _proxyUrl = _configuration["Binance:ProxyUrl"] ?? string.Empty;

        _httpClient.BaseAddress = new Uri("https://api.binance.com/");
        _timer = new Timer(_ => { _ = SyncDataCallbackAsync(); }, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("api/v3/ping");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Binance connection successful. Response: {Content}", content);
                return true;
            }

            _logger.LogWarning("Binance connection failed. Status: {StatusCode}, Content: {Content}",
                response.StatusCode, content);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Binance connection test failed");
            return false;
        }
    }

    public async Task<IEnumerable<TradingPairDto>> GetTopTradingPairsAsync(int count = 10)
    {
        try
        {
            var symbolsToQuery = _symbols.Take(count).ToArray();
            var symbolsJson = "[" + string.Join(",", symbolsToQuery.Select(s => $"\"{s}\"")) + "]";
            var symbolsParam = Uri.EscapeDataString(symbolsJson);
            var response = await _httpClient.GetStringAsync($"api/v3/ticker/24hr?symbols={symbolsParam}");
            var tickerDataList = JsonConvert.DeserializeObject<List<BinanceTickerData>>(response) ?? new();

            return tickerDataList.Select(t => new TradingPairDto
            {
                Symbol = t.Symbol,
                BaseAsset = t.Symbol.Replace("USDT", string.Empty),
                QuoteAsset = "USDT",
                Price = t.LastPrice,
                Change24h = t.PriceChangePercent / 100,
                Change24hPercent = t.PriceChangePercent,
                Volume24h = t.Volume,
                High24h = t.HighPrice,
                Low24h = t.LowPrice,
                LastUpdated = DateTime.UtcNow,
                IsActive = true
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get top trading pairs from Binance API");
            return Enumerable.Empty<TradingPairDto>();
        }
    }

    public async Task<TradingPairDto?> GetTradingPairAsync(string symbol)
    {
        try
        {
            var response = await _httpClient.GetStringAsync($"api/v3/ticker/24hr?symbol={symbol}");
            var t = JsonConvert.DeserializeObject<BinanceTickerData>(response);
            if (t == null) return null;

            return new TradingPairDto
            {
                Symbol = t.Symbol,
                BaseAsset = t.Symbol.Replace("USDT", string.Empty),
                QuoteAsset = "USDT",
                Price = t.LastPrice,
                Change24h = t.PriceChangePercent / 100,
                Change24hPercent = t.PriceChangePercent,
                Volume24h = t.Volume,
                High24h = t.HighPrice,
                Low24h = t.LowPrice,
                LastUpdated = DateTime.UtcNow,
                IsActive = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get trading pair {Symbol} from Binance", symbol);
            return null;
        }
    }

    public async Task<IEnumerable<KLineDataDto>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
    {
        try
        {
            var response = await _httpClient.GetStringAsync($"api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}");
            var klineData = JsonConvert.DeserializeObject<List<object[]>>(response) ?? new();

            return klineData.Select(k => new KLineDataDto
            {
                Symbol = symbol,
                TimeFrame = interval,
                OpenTime = Convert.ToInt64(k[0]),
                Open = Convert.ToDecimal(k[1]),
                High = Convert.ToDecimal(k[2]),
                Low = Convert.ToDecimal(k[3]),
                Close = Convert.ToDecimal(k[4]),
                Volume = Convert.ToDecimal(k[5]),
                CloseTime = Convert.ToInt64(k[6]),
                OpenDateTime = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(k[0])).UtcDateTime,
                CloseDateTime = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(k[6])).UtcDateTime
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get K-line data for {Symbol} {Interval} from Binance", symbol, interval);
            return Enumerable.Empty<KLineDataDto>();
        }
    }

    public async Task StartRealTimeDataSyncAsync()
    {
        _cts ??= new CancellationTokenSource();
        _logger.LogInformation("Starting Binance data sync with 5-second intervals");

        await SyncTradingPairsAsync(_cts.Token);
        await SyncKLineDataAsync(_cts.Token);

        _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public Task StopRealTimeDataSyncAsync()
    {
        _logger.LogInformation("Stopping Binance data sync");
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _cts?.Dispose();
    }

    private async Task SyncDataCallbackAsync()
    {
        if (Interlocked.Exchange(ref _isSyncing, 1) != 0)
        {
            _logger.LogDebug("Binance sync already running; skipping overlapping timer tick");
            return;
        }

        try
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            await SyncTradingPairsAsync(ct);
            await SyncKLineDataAsync(ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during data sync");
        }
        finally
        {
            Interlocked.Exchange(ref _isSyncing, 0);
        }
    }

    private async Task SyncTradingPairsAsync(CancellationToken ct)
    {
        try
        {
            var binancePairs = await GetTopTradingPairsAsync(_symbols.Length);
            foreach (var pair in binancePairs)
            {
                _priceUpdateBatchService.TryEnqueue(
                    pair.Symbol, pair.Price, pair.Change24h, pair.Volume24h, pair.High24h, pair.Low24h);
            }

            _logger.LogInformation("Successfully synced {Count} Binance trading pairs", binancePairs.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync trading pairs");
        }
    }

    private async Task SyncKLineDataAsync(CancellationToken ct)
    {
        try
        {
            var items = (
                from symbol in _symbols
                from interval in _intervals
                select (Symbol: symbol, Interval: interval)).ToArray();

            // [联网-官方: https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.parallel.foreachasync]
            await Parallel.ForEachAsync(items, new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxConcurrency,
                CancellationToken = ct
            }, async (item, token) =>
            {
                try
                {
                    var klineData = await GetKLineDataAsync(item.Symbol, item.Interval, 2);
                    var latestKline = klineData.LastOrDefault();
                    if (latestKline == null)
                    {
                        return;
                    }

                    var cacheKey = $"{item.Symbol}_{item.Interval}";
                    var isNewKLine = !_klineCache.TryGetValue(cacheKey, out var previous) ||
                                     previous.OpenTime != latestKline.OpenTime;

                    await _realTimeDataPush.PushKLineDataAsync(
                        item.Symbol, item.Interval, latestKline, isNewKLine);

                    _klineCache[cacheKey] = latestKline;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing K-line data for {Symbol} {Interval}", item.Symbol, item.Interval);
                }
            });

            var now = DateTime.UtcNow;
            var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            if (_lastKlineSaveMinute != currentMinute)
            {
                _lastKlineSaveMinute = currentMinute;
                await SaveCachedKLineDataToDatabaseAsync(currentMinute, ct);
            }

            _logger.LogInformation("K-line data sync completed for {SymbolCount} symbols and {IntervalCount} intervals",
                _symbols.Length, _intervals.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync K-line data");
        }
    }

    private async Task SaveCachedKLineDataToDatabaseAsync(DateTime currentMinute, CancellationToken ct)
    {
        if (_klineCache.IsEmpty)
        {
            _logger.LogDebug("No cached K-line data to save for minute {Minute}", currentMinute.ToString("HH:mm"));
            return;
        }

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var klineDataRepository = scope.ServiceProvider.GetRequiredService<IKLineDataRepository>();
            var entities = new List<KLineData>(_klineCache.Count);

            foreach (var kvp in _klineCache)
            {
                var tradingPairId = await GetTradingPairIdAsync(kvp.Value.Symbol, scope, ct);
                if (tradingPairId <= 0)
                {
                    continue;
                }

                entities.Add(new KLineData
                {
                    TradingPairId = tradingPairId,
                    TimeFrame = kvp.Value.TimeFrame,
                    OpenTime = kvp.Value.OpenTime,
                    CloseTime = kvp.Value.CloseTime,
                    Open = kvp.Value.Open,
                    High = kvp.Value.High,
                    Low = kvp.Value.Low,
                    Close = kvp.Value.Close,
                    Volume = kvp.Value.Volume
                });
            }

            if (entities.Count == 0)
            {
                return;
            }

            await klineDataRepository.SaveKLineDataBatchAsync(entities);
            _metrics.KLineSaved();
            _klineCache.Clear();

            _logger.LogInformation("保存缓存K线数据到数据库: {Count} 条记录, 时间: {Minute}",
                entities.Count, currentMinute.ToString("HH:mm"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存缓存K线数据到数据库失败: {Minute}", currentMinute.ToString("HH:mm"));
        }
    }

    private async Task<long> GetTradingPairIdAsync(string symbol, IServiceScope scope, CancellationToken ct)
    {
        if (_tradingPairIds.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        var repository = scope.ServiceProvider.GetService<ITradingPairRepository>();
        if (repository is null)
        {
            return 0;
        }

        var pair = await repository.GetBySymbolAsync(symbol);
        if (pair is null)
        {
            return 0;
        }

        _tradingPairIds[symbol] = pair.Id;
        return pair.Id;
    }

    private string[] ReadList(string key, string[] defaults)
    {
        var configured = _configuration.GetValue<string>(key);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return defaults;
        }

        var values = configured
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length > 0 ? values : defaults;
    }

    public class BinanceTickerData
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [JsonProperty("lastPrice")]
        public decimal LastPrice { get; set; }

        [JsonProperty("priceChange")]
        public decimal PriceChange { get; set; }

        [JsonProperty("priceChangePercent")]
        public decimal PriceChangePercent { get; set; }

        [JsonProperty("volume")]
        public decimal Volume { get; set; }

        [JsonProperty("highPrice")]
        public decimal HighPrice { get; set; }

        [JsonProperty("lowPrice")]
        public decimal LowPrice { get; set; }
    }
}
