using CryptoSpot.Domain.Entities;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces;
using CryptoSpot.Core.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace CryptoSpot.Infrastructure.ExternalServices
{
    /// <summary>
    /// Binance市场数据提供者 - 纯粹的业务服务，不继承BackgroundService
    /// </summary>
    public class BinanceMarketDataProvider : IMarketDataProvider
    {
        private readonly HttpClient _httpClient;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<BinanceMarketDataProvider> _logger;
        private readonly IConfiguration _configuration;
        private readonly Timer _timer;
        private readonly ConcurrentDictionary<string, DateTime> _lastUpdateTimes = new();
        private readonly ConcurrentDictionary<string, (decimal Price, decimal Change24h, decimal Volume24h, decimal High24h, decimal Low24h)> _priceCache = new();
        private readonly ConcurrentDictionary<string, KLineData> _klineCache = new();
        private DateTime _lastMinuteSave = DateTime.MinValue;

        private readonly string[] _topSymbols = { "BTCUSDT", "ETHUSDT", "SOLUSDT" };
        private readonly string[] _intervals = { "1m", "5m", "15m", "1h", "4h", "1d" };
        private readonly string _proxyUrl;

        public string ProviderName => "Binance";

        public BinanceMarketDataProvider(
            HttpClient httpClient,
            IServiceScopeFactory serviceScopeFactory,
            ILogger<BinanceMarketDataProvider> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _configuration = configuration;
            _proxyUrl = _configuration["Binance:ProxyUrl"] ?? "";

            _httpClient.BaseAddress = new Uri("https://api.binance.com/");
            
            if (!string.IsNullOrEmpty(_proxyUrl))
            {
                _logger.LogInformation("Using proxy for Binance API: {ProxyUrl}", _proxyUrl);
                ConfigureProxy();
            }
            
            _timer = new Timer(SyncDataCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        private void ConfigureProxy()
        {
            _logger.LogInformation("Proxy configuration: {ProxyUrl}", _proxyUrl);
        }

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                _logger.LogInformation("Testing connection to Binance API...");
                var response = await _httpClient.GetAsync("api/v3/ping");
                var content = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ Binance connection successful. Response: {Content}", content);
                    return true;
                }
                else
                {
                    _logger.LogWarning("⚠️ Binance connection failed. Status: {StatusCode}, Content: {Content}", 
                        response.StatusCode, content);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Binance connection test failed: {Message}", ex.Message);
                return false;
            }
        }

        public async Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int count = 5)
        {
            try
            {
                var symbolsToQuery = _topSymbols.Take(count).ToArray();
                var symbolsJson = "[" + string.Join(",", symbolsToQuery.Select(s => $"\"{s}\"")) + "]";
                var symbolsParam = Uri.EscapeDataString(symbolsJson);
                var url = $"api/v3/ticker/24hr?symbols={symbolsParam}";
                
                _logger.LogInformation("Fetching batch data for symbols: {Symbols}", string.Join(", ", symbolsToQuery));
                
                var response = await _httpClient.GetStringAsync(url);
                var tickerDataList = JsonConvert.DeserializeObject<List<BinanceTickerData>>(response);

                if (tickerDataList == null || !tickerDataList.Any())
                {
                    _logger.LogWarning("No ticker data received from Binance API");
                    return new List<TradingPair>();
                }

                _logger.LogInformation("Successfully received data for {Count} trading pairs", tickerDataList.Count);

                return tickerDataList.Select(t => new TradingPair
                {
                    Symbol = t.Symbol,
                    BaseAsset = t.Symbol.Replace("USDT", ""),
                    QuoteAsset = "USDT",
                    Price = t.LastPrice,
                    Change24h = t.PriceChangePercent / 100, // 转换为小数形式
                    Volume24h = t.Volume,
                    High24h = t.HighPrice,
                    Low24h = t.LowPrice,
                    LastUpdated = DateTimeExtensions.GetCurrentUnixTimeMilliseconds(),
                    IsActive = true
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get top trading pairs from Binance API");
                return new List<TradingPair>();
            }
        }

        public async Task<TradingPair?> GetTradingPairAsync(string symbol)
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"api/v3/ticker/24hr?symbol={symbol}");
                var tickerData = JsonConvert.DeserializeObject<BinanceTickerData>(response);

                if (tickerData == null) return null;

                return new TradingPair
                {
                    Symbol = tickerData.Symbol,
                    BaseAsset = tickerData.Symbol.Replace("USDT", ""),
                    QuoteAsset = "USDT",
                    Price = tickerData.LastPrice,
                    Change24h = tickerData.PriceChangePercent / 100, // 转换为小数形式
                    Volume24h = tickerData.Volume,
                    High24h = tickerData.HighPrice,
                    Low24h = tickerData.LowPrice,
                    LastUpdated = DateTimeExtensions.GetCurrentUnixTimeMilliseconds(),
                    IsActive = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get trading pair {Symbol} from Binance", symbol);
                return null;
            }
        }

        public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}");
                var klineData = JsonConvert.DeserializeObject<List<object[]>>(response);

                if (klineData == null) return new List<KLineData>();

                using var scope = _serviceScopeFactory.CreateScope();
                var priceDataService = scope.ServiceProvider.GetRequiredService<IPriceDataService>();
                var tradingPair = await priceDataService.GetCurrentPriceAsync(symbol);
                if (tradingPair == null) return new List<KLineData>();

                return klineData.Select(k => new KLineData
                {
                    TradingPairId = tradingPair.Id,
                    TimeFrame = interval,
                    OpenTime = Convert.ToInt64(k[0]),
                    CloseTime = Convert.ToInt64(k[6]),
                    Open = Convert.ToDecimal(k[1]),
                    High = Convert.ToDecimal(k[2]),
                    Low = Convert.ToDecimal(k[3]),
                    Close = Convert.ToDecimal(k[4]),
                    Volume = Convert.ToDecimal(k[5])
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get K-line data for {Symbol} {Interval} from Binance", symbol, interval);
                return new List<KLineData>();
            }
        }

        public async Task StartRealTimeDataSyncAsync()
        {
            _logger.LogInformation("Starting Binance data sync with 5-second intervals");
            
            await SyncTradingPairsAsync();
            await SyncKLineDataAsync();

            // 改为5秒更新一次，平衡实时性和数据库负载
            _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public async Task StopRealTimeDataSyncAsync()
        {
            _logger.LogInformation("Stopping Binance data sync");
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        private async void SyncDataCallback(object? state)
        {
            try
            {
                await SyncTradingPairsAsync();
                await SyncKLineDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data sync");
            }
        }

        private async Task SyncTradingPairsAsync()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var priceDataService = scope.ServiceProvider.GetRequiredService<IPriceDataService>();
                var realTimeDataPush = scope.ServiceProvider.GetService<IRealTimeDataPushService>();
                var binancePairs = await GetTopTradingPairsAsync();
                
                var priceUpdates = new Dictionary<string, object>();
                var now = DateTime.UtcNow;
                var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
                
                foreach (var binancePair in binancePairs)
                {
                    // 1. 实时推送所有价格数据
                    if (realTimeDataPush != null)
                    {
                        var priceData = new
                        {
                            symbol = binancePair.Symbol,
                            price = binancePair.Price,
                            change24h = binancePair.Change24h,
                            volume24h = binancePair.Volume24h,
                            high24h = binancePair.High24h,
                            low24h = binancePair.Low24h,
                            timestamp = DateTimeExtensions.GetCurrentUnixTimeMilliseconds()
                        };
                        
                        priceUpdates[binancePair.Symbol] = priceData;
                    }
                    
                    // 2. 缓存价格数据（不立即入库）
                    _priceCache[binancePair.Symbol] = (
                        binancePair.Price,
                        binancePair.Change24h,
                        binancePair.Volume24h,
                        binancePair.High24h,
                        binancePair.Low24h
                    );
                }
                
                // 3. 批量推送实时价格更新
                if (realTimeDataPush != null && priceUpdates.Count > 0)
                {
                    await realTimeDataPush.PushPriceDataToMultipleSymbolsAsync(priceUpdates);
                }
                
                // 4. 检查是否需要入库（每分钟结束时入库一次）
                if (_lastMinuteSave != currentMinute)
                {
                    await SaveCachedPricesToDatabaseAsync(priceDataService, currentMinute);
                    _lastMinuteSave = currentMinute;
                }
                
                _logger.LogInformation("Successfully synced {Count} trading pairs (real-time push)", binancePairs.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync trading pairs");
            }
        }

        /// <summary>
        /// 将缓存的价格数据保存到数据库（每分钟执行一次）
        /// </summary>
        private async Task SaveCachedPricesToDatabaseAsync(IPriceDataService priceDataService, DateTime currentMinute)
        {
            try
            {
                if (_priceCache.IsEmpty)
                {
                    _logger.LogDebug("No cached prices to save for minute {Minute}", currentMinute.ToString("HH:mm"));
                    return;
                }

                var tasks = new List<Task>();
                foreach (var kvp in _priceCache)
                {
                    var symbol = kvp.Key;
                    var (price, change24h, volume24h, high24h, low24h) = kvp.Value;
                    
                    tasks.Add(priceDataService.UpdateTradingPairPriceAsync(
                        symbol, price, change24h, volume24h, high24h, low24h));
                }

                await Task.WhenAll(tasks);
                
                _logger.LogInformation("💾 保存缓存价格数据到数据库: {Count} 个交易对, 时间: {Minute}", 
                    _priceCache.Count, currentMinute.ToString("HH:mm"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存缓存价格数据到数据库失败: {Minute}", currentMinute.ToString("HH:mm"));
            }
        }

        /// <summary>
        /// 判断是否为新K线时间
        /// </summary>
        private bool IsNewKLineTime(long openTime, string interval)
        {
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var intervalMs = GetIntervalMilliseconds(interval);
            
            // 如果当前时间距离K线开始时间超过间隔时间，认为是新K线
            return (currentTime - openTime) >= intervalMs;
        }

        /// <summary>
        /// 获取时间间隔对应的毫秒数
        /// </summary>
        private long GetIntervalMilliseconds(string interval)
        {
            return interval switch
            {
                "1m" => 60 * 1000,
                "5m" => 5 * 60 * 1000,
                "15m" => 15 * 60 * 1000,
                "1h" => 60 * 60 * 1000,
                "4h" => 4 * 60 * 60 * 1000,
                "1d" => 24 * 60 * 60 * 1000,
                _ => 60 * 1000 // 默认1分钟
            };
        }

        /// <summary>
        /// 将缓存的K线数据保存到数据库（每分钟执行一次）
        /// </summary>
        private async Task SaveCachedKLineDataToDatabaseAsync(DateTime currentMinute)
        {
            try
            {
                if (_klineCache.IsEmpty)
                {
                    _logger.LogDebug("No cached K-line data to save for minute {Minute}", currentMinute.ToString("HH:mm"));
                    return;
                }

                using var scope = _serviceScopeFactory.CreateScope();
                var klineDataService = scope.ServiceProvider.GetRequiredService<IKLineDataService>();
                
                var tasks = new List<Task>();
                foreach (var kvp in _klineCache)
                {
                    var klineData = kvp.Value;
                    tasks.Add(klineDataService.AddOrUpdateKLineDataAsync(klineData));
                }

                await Task.WhenAll(tasks);
                
                _logger.LogInformation("💾 保存缓存K线数据到数据库: {Count} 条记录, 时间: {Minute}", 
                    _klineCache.Count, currentMinute.ToString("HH:mm"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存缓存K线数据到数据库失败: {Minute}", currentMinute.ToString("HH:mm"));
            }
        }

        private async Task SyncKLineDataAsync()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var realTimeDataPush = scope.ServiceProvider.GetService<IRealTimeDataPushService>();
                
                var tasks = new List<Task>();
                var now = DateTime.UtcNow;
                var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
                
                foreach (var symbol in _topSymbols)
                {
                    foreach (var interval in _intervals)
                    {
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                var klineData = await GetKLineDataAsync(symbol, interval, 2);
                                var latestKline = klineData.LastOrDefault();
                                
                                if (latestKline != null)
                                {
                                    // 1. 实时推送所有K线数据
                                    if (realTimeDataPush != null)
                                    {
                                        // 判断是否为新K线（基于时间戳）
                                        bool isNewKLine = IsNewKLineTime(latestKline.OpenTime, interval);
                                        await realTimeDataPush.PushKLineDataAsync(symbol, interval, latestKline, isNewKLine);
                                    }
                                    
                                    // 2. 缓存K线数据（使用symbol+interval作为key）
                                    var cacheKey = $"{symbol}_{interval}";
                                    _klineCache[cacheKey] = latestKline;
                                }
                                
                                _logger.LogDebug("Synced K-line data for {Symbol} {Interval} (real-time push)", symbol, interval);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error syncing K-line data for {Symbol} {Interval}", symbol, interval);
                            }
                        });
                        
                        tasks.Add(task);
                    }
                }
                
                await Task.WhenAll(tasks);
                
                // 3. 检查是否需要入库K线数据（每分钟结束时入库一次）
                if (_lastMinuteSave != currentMinute)
                {
                    await SaveCachedKLineDataToDatabaseAsync(currentMinute);
                }
                
                _logger.LogInformation("K-line data sync completed for {SymbolCount} symbols and {IntervalCount} intervals (real-time push)", 
                    _topSymbols.Length, _intervals.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync K-line data");
            }
        }
    }

    // Binance API response models
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
