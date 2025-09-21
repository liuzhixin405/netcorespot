using Microsoft.AspNetCore.SignalR;
using CryptoSpot.API.Hubs;
using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces;
using CryptoSpot.Core.Interfaces.Trading;

namespace CryptoSpot.API.Services
{
    public class SignalRDataPushService : IRealTimeDataPushService
    {
        private readonly IHubContext<TradingHub> _hubContext;
        private readonly IOrderMatchingEngine _orderMatchingEngine;
        private readonly ILogger<SignalRDataPushService> _logger;

        public SignalRDataPushService(IHubContext<TradingHub> hubContext, IOrderMatchingEngine orderMatchingEngine, ILogger<SignalRDataPushService> logger)
        {
            _hubContext = hubContext;
            _orderMatchingEngine = orderMatchingEngine;
            _logger = logger;
        }

        public async Task PushKLineDataAsync(string symbol, string interval, KLineData klineData, bool isNewKLine = false)
        {
            try
            {
                var groupName = $"kline_{symbol}_{interval}";
                
                // 转换为前端期望的格式
                var klineUpdate = new
                {
                    symbol = symbol,
                    interval = interval,
                    timestamp = klineData.OpenTime,
                    open = klineData.Open,
                    high = klineData.High,
                    low = klineData.Low,
                    close = klineData.Close,
                    volume = klineData.Volume,
                    isNewKLine = isNewKLine
                };

                await _hubContext.Clients.Group(groupName).SendAsync("KLineUpdate", klineUpdate, isNewKLine);
                
                _logger.LogDebug($"Pushed KLine data for {symbol} {interval}: {klineData.Close}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to push KLine data for {symbol} {interval}");
            }
        }

        public async Task PushHistoricalKLineDataAsync(string symbol, string interval, IEnumerable<KLineData> historicalData)
        {
            try
            {
                var groupName = $"kline_{symbol}_{interval}";
                
                // 转换为前端期望的格式
                var historicalKLines = historicalData.Select(k => new
                {
                    symbol = symbol,
                    interval = interval,
                    timestamp = k.OpenTime,
                    open = k.Open,
                    high = k.High,
                    low = k.Low,
                    close = k.Close,
                    volume = k.Volume
                }).ToList();

                await _hubContext.Clients.Group(groupName).SendAsync("HistoricalKLineData", historicalKLines);
                
                _logger.LogDebug($"Pushed {historicalKLines.Count} historical KLine records for {symbol} {interval}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to push historical KLine data for {symbol} {interval}");
            }
        }

        public async Task PushPriceDataAsync(string symbol, object priceData)
        {
            try
            {
                var groupName = $"price_{symbol}";
                
                await _hubContext.Clients.Group(groupName).SendAsync("PriceUpdate", priceData);
                
                _logger.LogDebug($"Pushed price data for {symbol}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to push price data for {symbol}");
            }
        }

        public async Task PushPriceDataToMultipleSymbolsAsync(Dictionary<string, object> priceUpdates)
        {
            try
            {
                var tasks = priceUpdates.Select(async kvp =>
                {
                    var groupName = $"price_{kvp.Key}";
                    await _hubContext.Clients.Group(groupName).SendAsync("PriceUpdate", kvp.Value);
                });

                await Task.WhenAll(tasks);
                
                _logger.LogDebug($"Pushed price data for {priceUpdates.Count} symbols");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push price data for multiple symbols");
            }
        }

        public async Task PushOrderBookDataAsync(string symbol, int depth = 20)
        {
            try
            {
                var groupName = $"orderbook_{symbol}";
                
                // 获取最新订单簿数据
                var orderBookDepth = await _orderMatchingEngine.GetOrderBookDepthAsync(symbol, depth);
                if (orderBookDepth != null)
                {
                    var orderBookData = new
                    {
                        symbol = symbol,
                        bids = orderBookDepth.Bids.Select(b => new
                        {
                            price = b.Price,
                            amount = b.Quantity,
                            total = b.Total
                        }).ToList(),
                        asks = orderBookDepth.Asks.Select(a => new
                        {
                            price = a.Price,
                            amount = a.Quantity,
                            total = a.Total
                        }).ToList(),
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    await _hubContext.Clients.Group(groupName).SendAsync("OrderBookUpdate", orderBookData);
                    
                    _logger.LogDebug($"Pushed order book data for {symbol}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to push order book data for {symbol}");
            }
        }
    }
}
