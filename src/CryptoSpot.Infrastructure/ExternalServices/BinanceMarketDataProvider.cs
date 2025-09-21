using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces.System;
using CryptoSpot.Core.Interfaces;

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
                    Change24h = t.PriceChange,
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
                    Change24h = tickerData.PriceChange,
                    Volume24h = tickerData.Volume,
                    High24h = tickerData.HighPrice,
                    Low24h = tickerData.LowPrice,
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
            _logger.LogInformation("Starting Binance data sync");
            
            await SyncTradingPairsAsync();
            await SyncKLineDataAsync();

            _timer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
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
                
                foreach (var binancePair in binancePairs)
                {
                    await priceDataService.UpdateTradingPairPriceAsync(
                        binancePair.Symbol,
                        binancePair.Price,
                        binancePair.Change24h,
                        binancePair.Volume24h,
                        binancePair.High24h,
                        binancePair.Low24h);
                    
                    // 准备实时推送数据
                    if (realTimeDataPush != null)
                    {
                        var priceData = new
                        {
                            symbol = binancePair.Symbol,
                            price = binancePair.Price,
                            change24h = binancePair.Change24h,
                            volume24h = binancePair.Volume24h,
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                        
                        priceUpdates[binancePair.Symbol] = priceData;
                    }
                }
                
                // 批量推送价格更新
                if (realTimeDataPush != null && priceUpdates.Count > 0)
                {
                    await realTimeDataPush.PushPriceDataToMultipleSymbolsAsync(priceUpdates);
                }
                
                _logger.LogInformation("Successfully synced {Count} trading pairs", binancePairs.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync trading pairs");
            }
        }

        private async Task SyncKLineDataAsync()
        {
            try
            {
                var tasks = new List<Task>();
                
                foreach (var symbol in _topSymbols)
                {
                    foreach (var interval in _intervals)
                    {
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                using var taskScope = _serviceScopeFactory.CreateScope();
                                var klineDataService = taskScope.ServiceProvider.GetRequiredService<IKLineDataService>();
                                var realTimeDataPush = taskScope.ServiceProvider.GetService<IRealTimeDataPushService>();
                                
                                var klineData = await GetKLineDataAsync(symbol, interval, 2);
                                var latestKline = klineData.LastOrDefault();
                                
                                if (latestKline != null)
                                {
                                    var existingKLine = await klineDataService.GetLatestKLineDataAsync(symbol, interval);
                                    bool isNewKLine = existingKLine == null || existingKLine.OpenTime != latestKline.OpenTime;
                                    
                                    bool hasChanged = existingKLine == null || 
                                                    existingKLine.Close != latestKline.Close ||
                                                    existingKLine.High != latestKline.High ||
                                                    existingKLine.Low != latestKline.Low ||
                                                    existingKLine.Volume != latestKline.Volume;
                                    
                                    if (hasChanged)
                                    {
                                        await klineDataService.AddOrUpdateKLineDataAsync(latestKline);
                                        _logger.LogDebug("Updated K-line data for {Symbol}: {OpenTime}, Close: {Close}", 
                                            symbol, latestKline.OpenTime, latestKline.Close);
                                        
                                        // 实时推送 K线数据
                                        if (realTimeDataPush != null)
                                        {
                                            await realTimeDataPush.PushKLineDataAsync(symbol, interval, latestKline, isNewKLine);
                                        }
                                    }
                                }
                                
                                _logger.LogDebug("Synced K-line data for {Symbol} {Interval}", symbol, interval);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to sync K-line data for {Symbol} {Interval}", symbol, interval);
                            }
                        });
                        
                        tasks.Add(task);
                    }
                }
                
                await Task.WhenAll(tasks);
                
                _logger.LogInformation("K-line data sync completed for {SymbolCount} symbols and {IntervalCount} intervals", 
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

        [JsonProperty("volume")]
        public decimal Volume { get; set; }

        [JsonProperty("highPrice")]
        public decimal HighPrice { get; set; }

        [JsonProperty("lowPrice")]
        public decimal LowPrice { get; set; }
    }
}
