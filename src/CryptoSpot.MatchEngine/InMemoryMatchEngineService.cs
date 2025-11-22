using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.MatchEngine.Core;
using CryptoSpot.MatchEngine.Events;

namespace CryptoSpot.MatchEngine
{
    /// <summary>
    /// 简单的内存撮合引擎实现（价时优先），撮合后使用 Redis Lua 脚本保证资产结算的原子性。
    /// 这是一个最小可用版本，用于替换或与 Redis-first 引擎并行验证。
    /// </summary>
    // Implements the application-level IMatchEngineService used by the rest of the system.
    public class InMemoryMatchEngineService : CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService
    {
        private readonly ILogger<InMemoryMatchEngineService> _logger;
        private readonly IServiceProvider _sp;
        private readonly ConcurrentDictionary<string, IOrderBook> _books = new();
        private readonly ISettlementService _settlement;
        private readonly IMatchEngineEventBus _eventBus;
        private readonly IMatchingAlgorithm _algorithm;
        private readonly IMatchEngineMetrics _metrics;

        public InMemoryMatchEngineService(ILogger<InMemoryMatchEngineService> logger, IServiceProvider sp, ISettlementService settlement, IMatchEngineEventBus eventBus, IMatchingAlgorithm algorithm, IMatchEngineMetrics metrics)
        {
            _logger = logger;
            _sp = sp;
            _settlement = settlement;
            _eventBus = eventBus;
            _algorithm = algorithm;
            _metrics = metrics;
        }

        public async Task<Order> PlaceOrderAsync(Order order, string symbol)
        {
            // basic validation
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (string.IsNullOrEmpty(symbol)) throw new ArgumentNullException(nameof(symbol));

            var book = _books.GetOrAdd(symbol, s => new InMemoryOrderBook(s));

            // per-symbol lock
            var sem = (SemaphoreSlim)((InMemoryOrderBook)book).SyncRoot;
            await sem.WaitAsync();
            try
            {
                // freeze assets via Redis asset repo
                using var scope = _sp.CreateScope();
                var redisAssets = scope.ServiceProvider.GetRequiredService<RedisAssetRepository>();
                var redisOrders = scope.ServiceProvider.GetRequiredService<RedisOrderRepository>();
                var redis = scope.ServiceProvider.GetRequiredService<IRedisCache>();

                var (currency, amount) = GetFreezeAmount(order, symbol);
                var userId = order.UserId ?? throw new InvalidOperationException("订单缺少用户ID");
                // pass `symbol` as tag so asset keys used in Lua scripts share the same cluster hash slot
                var freezeSuccess = await redisAssets.FreezeAssetAsync(userId, currency, amount, symbol);
                if (!freezeSuccess)
                {
                    throw new InvalidOperationException($"余额不足：需要 {amount} {currency}");
                }


                // insert order into in-memory book and persist order into redis repository for global visibility
                order.Status = OrderStatus.Active;
                book.Add(order);

                // persist order before matching so that UpdateOrderStatusAsync and other Redis-side operations
                // can find the order record during matching
                await redisOrders.CreateOrderAsync(order, symbol);

                // attempt matching
                var trades = await RunMatchingAsync(order, book, symbol, redis, redisOrders);

                // 如果没有发生任何成交，推送一次订单簿更新，保证订阅者看到新增挂单
                if (trades.Count == 0)
                {
                    try
                    {
                        await PushOrderBookUpdate(symbol);
                        await _eventBus.PublishAsync(new OrderPlacedEvent(symbol, order));
                        await _eventBus.PublishAsync(new OrderBookChangedEvent(symbol));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to push order book update after placing order: {Symbol}", symbol);
                    }
                }

                return order;
            }
            finally
            {
                sem.Release();
            }
        }

        private async Task<List<Trade>> RunMatchingAsync(Order taker, IOrderBook book, string symbol, IRedisCache redis, RedisOrderRepository redisOrders)
        {
            var trades = new List<Trade>();
            foreach (var slice in _algorithm.Match(book, taker))
            {
                _metrics.ObserveMatchAttempt(symbol, slice.Quantity, slice.Price);
                var start = DateTime.UtcNow;
                var settle = await _settlement.SettleAsync(new SettlementContext(slice.Maker, slice.Taker, slice.Price, slice.Quantity, symbol));
                _metrics.ObserveSettlement(symbol, settle.Success, (long)(DateTime.UtcNow - start).TotalMilliseconds);
                if (!settle.Success)
                {
                    _logger.LogWarning("Settlement failed, abort remaining matches: {Symbol} Maker={Maker} Taker={Taker}", symbol, slice.Maker.Id, slice.Taker.Id);
                    break;
                }

                var trade = new Trade
                {
                    Id = await redis.StringIncrementAsync("global:trade_id"),
                    TradingPairId = taker.TradingPairId,
                    BuyOrderId = taker.Side == OrderSide.Buy ? taker.Id : slice.Maker.Id,
                    SellOrderId = taker.Side == OrderSide.Sell ? taker.Id : slice.Maker.Id,
                    Price = slice.Price,
                    Quantity = slice.Quantity,
                    BuyerId = taker.Side == OrderSide.Buy ? taker.UserId ?? 0 : slice.Maker.UserId ?? 0,
                    SellerId = taker.Side == OrderSide.Sell ? taker.UserId ?? 0 : slice.Maker.UserId ?? 0,
                    ExecutedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                trades.Add(trade);

                await SaveTradeToRedis(trade, symbol, redis);

                var makerStatus = slice.Maker.FilledQuantity >= slice.Maker.Quantity ? OrderStatus.Filled : OrderStatus.PartiallyFilled;
                var takerStatus = taker.FilledQuantity >= taker.Quantity ? OrderStatus.Filled : (taker.FilledQuantity > 0 ? OrderStatus.PartiallyFilled : OrderStatus.Active);
                await redisOrders.UpdateOrderStatusAsync(slice.Maker.Id, makerStatus, slice.Maker.FilledQuantity);
                await redisOrders.UpdateOrderStatusAsync(taker.Id, takerStatus, taker.FilledQuantity);

                try {
                    await _eventBus.PublishAsync(new TradeExecutedEvent(symbol, trade, slice.Maker, taker));
                    await _eventBus.PublishAsync(new OrderBookChangedEvent(symbol));
                } catch { }

                if (taker.FilledQuantity >= taker.Quantity) break;
            }
            return trades;
        }


        private async Task SaveTradeToRedis(Trade trade, string symbol, IRedisCache redis)
        {
            var key = $"trade:{trade.Id}";
            await redis.HMSetAsync(key,
                "id", trade.Id.ToString(),
                "tradingPairId", trade.TradingPairId.ToString(),
                "buyOrderId", trade.BuyOrderId.ToString(),
                "sellOrderId", trade.SellOrderId.ToString(),
                "price", trade.Price.ToString("F8"),
                "quantity", trade.Quantity.ToString("F8"),
                "buyerId", trade.BuyerId.ToString(),
                "sellerId", trade.SellerId.ToString(),
                "executedAt", trade.ExecutedAt.ToString());

            await redis.ListLeftPushAsync($"trades:{symbol}", trade.Id.ToString());
            await redis.LTrimAsync($"trades:{symbol}", 0, 999);

            var json = System.Text.Json.JsonSerializer.Serialize(new { tradeId = trade.Id, operation = "CREATE", timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            await redis.ListLeftPushAsync("sync_queue:trades", json);

            // push realtime via scoped service
            try
            {
                using var scope = _sp.CreateScope();
                var realTimePush = scope.ServiceProvider.GetService<CryptoSpot.Application.Abstractions.Services.RealTime.IRealTimeDataPushService>();
                if (realTimePush != null)
                {
                    var tradeDto = new CryptoSpot.Application.DTOs.Trading.TradeDto
                    {
                        Id = trade.Id,
                        Symbol = symbol,
                        Price = trade.Price,
                        Quantity = trade.Quantity,
                        BuyOrderId = trade.BuyOrderId,
                        SellOrderId = trade.SellOrderId,
                        BuyerId = trade.BuyerId,
                        SellerId = trade.SellerId,
                        ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(trade.ExecutedAt).DateTime,
                        TotalValue = trade.Price * trade.Quantity
                    };
                    await realTimePush.PushUserTradeAsync(trade.BuyerId, tradeDto);
                    await realTimePush.PushUserTradeAsync(trade.SellerId, tradeDto);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push trade realtime");
            }
        }

        private (string currency, decimal amount) GetFreezeAmount(Order order, string symbol)
        {
            var quoteCurrency = "USDT";
            var baseCurrency = symbol.Replace(quoteCurrency, "");
            var price = order.Price ?? 0;
            if (order.Side == OrderSide.Buy)
            {
                return (quoteCurrency, order.Quantity * price);
            }
            else
            {
                return (baseCurrency, order.Quantity);
            }
        }

        private (string baseCurrency, string quoteCurrency) ParseSymbol(string symbol)
        {
            var quoteCurrency = "USDT";
            var baseCurrency = symbol.Replace(quoteCurrency, "");
            return (baseCurrency, quoteCurrency);
        }

        // 内部 OrderBook 类已抽离为 InMemoryOrderBook

        /// <summary>
        /// 推送订单簿快照给实时推送服务（SignalR 等）。
        /// </summary>
        private async Task PushOrderBookUpdate(string symbol)
        {
            try
            {
                // 获取聚合深度
                var bids = await GetAggregatedDepth(symbol, OrderSide.Buy, 20);
                var asks = await GetAggregatedDepth(symbol, OrderSide.Sell, 20);

                var bidDtos = bids.Select(x => new CryptoSpot.Application.DTOs.Trading.OrderBookLevelDto { Price = x.price, Quantity = x.quantity }).ToList();
                var askDtos = asks.Select(x => new CryptoSpot.Application.DTOs.Trading.OrderBookLevelDto { Price = x.price, Quantity = x.quantity }).ToList();

                using var scope = _sp.CreateScope();
                var realTimePush = scope.ServiceProvider.GetService<CryptoSpot.Application.Abstractions.Services.RealTime.IRealTimeDataPushService>();
                if (realTimePush != null)
                {
                    await realTimePush.PushExternalOrderBookSnapshotAsync(symbol, bidDtos, askDtos, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push order book snapshot for {Symbol}", symbol);
            }
        }

        private async Task<List<(decimal price, decimal quantity)>> GetAggregatedDepth(string symbol, OrderSide side, int depth)
        {
            // Query Redis repository to get aggregated depth to keep snapshot consistent with Redis-backed view
            using var scope = _sp.CreateScope();
            var redisOrders = scope.ServiceProvider.GetRequiredService<RedisOrderRepository>();
            return await redisOrders.GetOrderBookDepthAsync(symbol, depth).ContinueWith(t => side == OrderSide.Buy ? t.Result.bids : t.Result.asks);
        }
    }
}
