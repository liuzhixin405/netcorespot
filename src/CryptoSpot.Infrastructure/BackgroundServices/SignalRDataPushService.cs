using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Hubs;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.RealTime; // migrated
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.Mapping;

namespace CryptoSpot.Infrastructure.BackgroundServices
{
    public class SignalRDataPushService : IRealTimeDataPushService, IExternalOrderBookSnapshotProvider
    {
        private readonly IHubContext<TradingHub> _hubContext;
        private readonly ILogger<SignalRDataPushService> _logger;
        private readonly IDtoMappingService _mapping;
        private readonly ConcurrentDictionary<string, ExternalOrderBookState> _externalOrderBooks =
            new(StringComparer.OrdinalIgnoreCase);

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
                
                _logger.LogDebug("Pushed KLine data for {Symbol} {Interval}: {Close}", symbol, interval, klineData.Close);
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
                
                _logger.LogDebug("Pushed price data for {Symbol}", symbol);
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
                
                _logger.LogDebug("Pushed price data for {Count} symbols", priceUpdates.Count);
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
                
                _logger.LogDebug("Pushed order book snapshot for {Symbol}: Bids={BidCount}, Asks={AskCount}", symbol, orderBookDepth.Bids.Count, orderBookDepth.Asks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to push order book data for {symbol}");
            }
        }

        public Task PushOrderBookDeltaAsync(string symbol, List<OrderBookLevelDto> bidChanges, List<OrderBookLevelDto> askChanges)
            => PushOrderBookDeltaToGroupAsync(
                symbol, $"orderbook_{symbol}", bidChanges, askChanges,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        public Task PushExternalOrderBookDeltaAsync(
            string symbol,
            List<OrderBookLevelDto> bidChanges,
            List<OrderBookLevelDto> askChanges,
            long timestamp)
            => PushExternalOrderBookDeltaCoreAsync(symbol, bidChanges, askChanges, timestamp);

        private async Task PushExternalOrderBookDeltaCoreAsync(
            string symbol,
            List<OrderBookLevelDto> bidChanges,
            List<OrderBookLevelDto> askChanges,
            long timestamp)
        {
            var book = _externalOrderBooks.GetOrAdd(symbol, static _ => new ExternalOrderBookState());
            await book.PublishGate.WaitAsync();
            try
            {
                var sequence = book.ApplyDelta(bidChanges, askChanges, timestamp);
                await PushOrderBookDeltaToGroupAsync(
                    symbol, $"external_orderbook_{symbol}", bidChanges, askChanges, timestamp, sequence);
            }
            finally
            {
                book.PublishGate.Release();
            }
        }

        private async Task PushOrderBookDeltaToGroupAsync(
            string symbol,
            string groupName,
            List<OrderBookLevelDto> bidChanges,
            List<OrderBookLevelDto> askChanges,
            long timestamp,
            long? sequence = null)
        {
            try
            {
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
                    timestamp,
                    sequence
                };

                await _hubContext.Clients.Group(groupName).SendAsync("OrderBookUpdate", deltaData);
                
                _logger.LogDebug("Pushed order book delta for {Symbol}: {Bids} bids, {Asks} asks", symbol, bidChanges?.Count ?? 0, askChanges?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to push order book delta for {symbol}");
            }
        }

        public async Task PushExternalOrderBookSnapshotAsync(string symbol, IReadOnlyList<OrderBookLevelDto> bids, IReadOnlyList<OrderBookLevelDto> asks, long timestamp)
        {
            var book = _externalOrderBooks.GetOrAdd(symbol, static _ => new ExternalOrderBookState());
            await book.PublishGate.WaitAsync();
            try
            {
                var sequence = book.SetSnapshot(bids, asks, timestamp);
                var groupName = $"external_orderbook_{symbol}";
                var data = new
                {
                    type = "snapshot",
                    symbol,
                    bids = bids.Select(b => new { price = b.Price, amount = b.Quantity, total = b.Total }).ToList(),
                    asks = asks.Select(a => new { price = a.Price, amount = a.Quantity, total = a.Total }).ToList(),
                    timestamp,
                    sequence
                };
                await _hubContext.Clients.Group(groupName).SendAsync("OrderBookData", data);
                _logger.LogDebug("Pushed external order book snapshot for {Symbol}", symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push external order book snapshot for {Symbol}", symbol);
            }
            finally
            {
                book.PublishGate.Release();
            }
        }

        public ExternalOrderBookSnapshot? GetSnapshot(string symbol)
        {
            return _externalOrderBooks.TryGetValue(symbol, out var book)
                ? book.GetSnapshot(symbol)
                : null;
        }

        public void RemoveExternalOrderBookSnapshot(string symbol)
        {
            _externalOrderBooks.TryRemove(symbol, out _);
        }

        private sealed class ExternalOrderBookState
        {
            private readonly object _sync = new();
            private Dictionary<decimal, OrderBookLevelDto> _bids = new();
            private Dictionary<decimal, OrderBookLevelDto> _asks = new();
            private long _sequence;
            private long _timestamp;
            private bool _hasSnapshot;

            public SemaphoreSlim PublishGate { get; } = new(1, 1);

            public long SetSnapshot(
                IReadOnlyList<OrderBookLevelDto> bids,
                IReadOnlyList<OrderBookLevelDto> asks,
                long timestamp)
            {
                lock (_sync)
                {
                    _bids = bids.Where(level => level.Quantity > 0)
                        .ToDictionary(level => level.Price, CopyLevel);
                    _asks = asks.Where(level => level.Quantity > 0)
                        .ToDictionary(level => level.Price, CopyLevel);
                    Trim(_bids, bids: true);
                    Trim(_asks, bids: false);
                    _timestamp = timestamp;
                    _hasSnapshot = true;
                    return ++_sequence;
                }
            }

            public long ApplyDelta(
                IReadOnlyList<OrderBookLevelDto> bids,
                IReadOnlyList<OrderBookLevelDto> asks,
                long timestamp)
            {
                lock (_sync)
                {
                    if (!_hasSnapshot)
                    {
                        return _sequence;
                    }

                    Apply(_bids, bids);
                    Apply(_asks, asks);
                    Trim(_bids, bids: true);
                    Trim(_asks, bids: false);
                    _timestamp = timestamp;
                    return ++_sequence;
                }
            }

            public ExternalOrderBookSnapshot? GetSnapshot(string symbol)
            {
                lock (_sync)
                {
                    if (!_hasSnapshot)
                    {
                        return null;
                    }

                    var bids = _bids.Values.OrderByDescending(level => level.Price)
                        .Select(CopyLevel).ToList();
                    var asks = _asks.Values.OrderBy(level => level.Price)
                        .Select(CopyLevel).ToList();
                    return new ExternalOrderBookSnapshot(symbol, bids, asks, _timestamp, _sequence);
                }
            }

            private static void Apply(
                Dictionary<decimal, OrderBookLevelDto> levels,
                IReadOnlyList<OrderBookLevelDto> updates)
            {
                foreach (var level in updates)
                {
                    if (level.Quantity <= 0)
                    {
                        levels.Remove(level.Price);
                    }
                    else
                    {
                        levels[level.Price] = CopyLevel(level);
                    }
                }
            }

            private static void Trim(Dictionary<decimal, OrderBookLevelDto> levels, bool bids)
            {
                while (levels.Count > 400)
                {
                    levels.Remove(bids ? levels.Keys.Min() : levels.Keys.Max());
                }
            }

            private static OrderBookLevelDto CopyLevel(OrderBookLevelDto level) => new()
            {
                Price = level.Price,
                Quantity = level.Quantity,
                Total = level.Price * level.Quantity,
                OrderCount = level.OrderCount
            };
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
                _logger.LogDebug("Pushed trade to group {GroupName}: TradeId={TradeId}", groupName, trade.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push trade data for {Symbol}", symbol);
            }
        }

        public async Task PushUserOrderUpdateAsync(long userId, OrderDto order)
        {
            try
            {
                var userGroup = $"user_{userId}";
                await _hubContext.Clients.Group(userGroup).SendAsync("OrderUpdate", order);
                _logger.LogDebug("Pushed order update to user {UserId}: OrderId={OrderId}", userId, order.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push order update to user {UserId}", userId);
            }
        }

        public async Task PushUserTradeAsync(long userId, TradeDto trade)
        {
            try
            {
                var userGroup = $"user_{userId}";
                await _hubContext.Clients.Group(userGroup).SendAsync("UserTradeUpdate", trade);
                _logger.LogDebug("Pushed user trade to user {UserId}: TradeId={TradeId}", userId, trade.TradeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push trade to user {UserId}", userId);
            }
        }

        public async Task PushUserAssetUpdateAsync(long userId, IEnumerable<AssetDto> assets)
        {
            try
            {
                var userGroup = $"user_{userId}";
                await _hubContext.Clients.Group(userGroup).SendAsync("AssetUpdate", assets);
                _logger.LogDebug("Pushed asset update to user {UserId}: {Count} assets", userId, assets.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push asset update to user {UserId}", userId);
            }
        }
    }
}
