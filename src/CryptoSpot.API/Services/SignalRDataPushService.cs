using Microsoft.AspNetCore.SignalR;
using CryptoSpot.API.Hubs;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.RealTime; // migrated
using CryptoSpot.Application.Abstractions.Trading; // migrated

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

        public async Task PushOrderBookDataAsync(string symbol, int depth = 5)
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
                        type = "snapshot", // 标记为快照数据
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

                    await _hubContext.Clients.Group(groupName).SendAsync("OrderBookData", orderBookData);
                    
                    _logger.LogDebug($"Pushed order book snapshot for {symbol}: Bids={orderBookDepth.Bids.Count}, Asks={orderBookDepth.Asks.Count}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to push order book data for {symbol}");
            }
        }

        public async Task PushOrderBookDeltaAsync(string symbol, List<OrderBookLevel> bidChanges, List<OrderBookLevel> askChanges)
        {
            try
            {
                var groupName = $"orderbook_{symbol}";
                
                var deltaData = new
                {
                    type = "delta", // 标记为增量更新
                    symbol = symbol,
                    bids = bidChanges?.Select(b => new
                    {
                        price = b.Price,
                        amount = b.Quantity,
                        total = b.Total
                    }).Cast<object>().ToList() ?? new List<object>(),
                    asks = askChanges?.Select(a => new
                    {
                        price = a.Price,
                        amount = a.Quantity,
                        total = a.Total
                    }).Cast<object>().ToList() ?? new List<object>(),
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                await _hubContext.Clients.Group(groupName).SendAsync("OrderBookUpdate", deltaData);
                
                _logger.LogDebug($"Pushed order book delta for {symbol}: {bidChanges?.Count ?? 0} bids, {askChanges?.Count ?? 0} asks");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to push order book delta for {symbol}");
            }
        }

        public async Task PushExternalOrderBookSnapshotAsync(string symbol, IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp)
        {
            try
            {
                var groupName = $"orderbook_{symbol}";
                var data = new
                {
                    type = "snapshot",
                    symbol,
                    bids = bids.Select(b => new { price = b.Price, amount = b.Quantity, total = b.Total }).ToList(),
                    asks = asks.Select(a => new { price = a.Price, amount = a.Quantity, total = a.Total }).ToList(),
                    timestamp
                };
                await _hubContext.Clients.Group(groupName).SendAsync("OrderBookData", data);
                _logger.LogDebug("Pushed external order book snapshot for {Symbol}", symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push external order book snapshot for {Symbol}", symbol);
            }
        }
    }
}
