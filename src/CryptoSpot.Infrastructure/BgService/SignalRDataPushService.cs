using Microsoft.AspNetCore.SignalR;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Hubs;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.RealTime; // migrated
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.Mapping;

namespace CryptoSpot.Infrastructure.BgServices
{
    public class SignalRDataPushService : IRealTimeDataPushService
    {
        private readonly IHubContext<TradingHub> _hubContext;
        private readonly ILogger<SignalRDataPushService> _logger;
        private readonly IDtoMappingService _mapping;

        public SignalRDataPushService(IHubContext<TradingHub> hubContext,ILogger<SignalRDataPushService> logger, IDtoMappingService mapping)
        {
            _hubContext = hubContext;
            _logger = logger;
            _mapping = mapping;
        }

        public async Task PushKLineDataAsync(string symbol, string interval, KLineDataDto klineData, bool isNewKLine = false)
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

        public async Task PushOrderBookDataAsync(string symbol, OrderBookDepthDto orderBookDepth)
        {
            try
            {
                var groupName = $"orderbook_{symbol}";
                
                // 获取最新订单簿数据
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
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to push order book data for {symbol}");
            }
        }

        public async Task PushOrderBookDeltaAsync(string symbol, List<OrderBookLevelDto> bidChanges, List<OrderBookLevelDto> askChanges)
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

        public async Task PushExternalOrderBookSnapshotAsync(string symbol, IReadOnlyList<OrderBookLevelDto> bids, IReadOnlyList<OrderBookLevelDto> asks, long timestamp)
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

        // 新增
        public async Task PushLastTradeAndMidPriceAsync(string symbol, decimal? lastPrice, decimal? lastQuantity, decimal? bestBid, decimal? bestAsk, decimal? midPrice, long timestamp)
        {
            try
            {
                var groupName = $"ticker_{symbol}"; // 单独分组，前端可选择订阅
                var data = new {
                    symbol,
                    lastPrice,
                    lastQuantity,
                    bestBid,
                    bestAsk,
                    midPrice,
                    timestamp
                };
                await _hubContext.Clients.Group(groupName).SendAsync("LastTradeAndMid", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push last trade & mid price for {Symbol}", symbol);
            }
        }

        public async Task PushTradeDataAsync(string symbol, MarketTradeDto trade)
        {
            try
            {
                var groupName = $"trades_{symbol}";
                var tradeData = new
                {
                    id = trade.Id,
                    symbol = trade.Symbol,
                    price = trade.Price,
                    quantity = trade.Quantity,
                    executedAt = trade.ExecutedAt,
                    isBuyerMaker = trade.IsBuyerMaker,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                await _hubContext.Clients.Group(groupName).SendAsync("TradeUpdate", tradeData);
                _logger.LogInformation("✅ [SignalR] 推送成交到组 {GroupName}: TradeId={TradeId}, Price={Price}, Quantity={Quantity}", 
                    groupName, trade.Id, trade.Price, trade.Quantity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push trade data for {Symbol}", symbol);
            }
        }

        public async Task PushUserOrderUpdateAsync(int userId, OrderDto order)
        {
            try
            {
                var userGroup = $"user_{userId}";
                await _hubContext.Clients.Group(userGroup).SendAsync("OrderUpdate", order);
                _logger.LogInformation("✅ [SignalR] 推送订单更新到用户 {UserId}: OrderId={OrderId}, Status={Status}", 
                    userId, order.OrderId, order.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push order update to user {UserId}", userId);
            }
        }

        public async Task PushUserTradeAsync(int userId, TradeDto trade)
        {
            try
            {
                var userGroup = $"user_{userId}";
                await _hubContext.Clients.Group(userGroup).SendAsync("UserTradeUpdate", trade);
                _logger.LogInformation("✅ [SignalR] 推送用户成交到用户 {UserId}: TradeId={TradeId}", userId, trade.TradeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push trade to user {UserId}", userId);
            }
        }

        public async Task PushUserAssetUpdateAsync(int userId, IEnumerable<AssetDto> assets)
        {
            try
            {
                var userGroup = $"user_{userId}";
                await _hubContext.Clients.Group(userGroup).SendAsync("AssetUpdate", assets);
                _logger.LogInformation("✅ [SignalR] 推送资产更新到用户 {UserId}: {Count} 个资产", userId, assets.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push asset update to user {UserId}", userId);
            }
        }
    }
}
