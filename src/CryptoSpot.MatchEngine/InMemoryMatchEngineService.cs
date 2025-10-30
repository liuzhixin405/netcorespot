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
        private readonly ConcurrentDictionary<string, OrderBook> _books = new();

        public InMemoryMatchEngineService(ILogger<InMemoryMatchEngineService> logger, IServiceProvider sp)
        {
            _logger = logger;
            _sp = sp;
        }

        public async Task<Order> PlaceOrderAsync(Order order, string symbol)
        {
            // basic validation
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (string.IsNullOrEmpty(symbol)) throw new ArgumentNullException(nameof(symbol));

            var book = _books.GetOrAdd(symbol, _ => new OrderBook(symbol));

            // per-symbol lock
            await book.Lock.WaitAsync();
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
                book.AddOrder(order);

                // persist order before matching so that UpdateOrderStatusAsync and other Redis-side operations
                // can find the order record during matching
                await redisOrders.CreateOrderAsync(order, symbol);

                // attempt matching
                var trades = await MatchAsync(order, book, symbol, redis, redisOrders);

                // 如果没有发生任何成交，推送一次订单簿更新，保证订阅者看到新增挂单
                if (trades.Count == 0)
                {
                    try
                    {
                        await PushOrderBookUpdate(symbol);
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
                book.Lock.Release();
            }
        }

        private async Task<List<Trade>> MatchAsync(Order taker, OrderBook book, string symbol, IRedisCache redis, RedisOrderRepository redisOrders)
        {
            var trades = new List<Trade>();

            while (taker.FilledQuantity < taker.Quantity)
            {
                var maker = book.GetBestOpposite(taker.Side);
                if (maker == null) break;

                // price check for limit orders
                if (taker.Type == OrderType.Limit && maker.Price.HasValue && taker.Price.HasValue)
                {
                    if (taker.Side == OrderSide.Buy && taker.Price < maker.Price) break;
                    if (taker.Side == OrderSide.Sell && taker.Price > maker.Price) break;
                }

                // prevent self trade
                if (maker.UserId == taker.UserId)
                {
                    // remove maker from orderbook to avoid infinite loop
                    book.RemoveOrder(maker);
                    continue;
                }

                var matched = Math.Min(taker.Quantity - taker.FilledQuantity, maker.Quantity - maker.FilledQuantity);
                var price = maker.Price ?? taker.Price ?? 0;

                // Attempt atomic settlement via Lua script (same logic as RedisOrderMatchingEngine)
                var success = await ExecuteTradeAssetsAtomicAsync(maker, taker, price, matched, symbol, redis);
                if (!success)
                {
                    _logger.LogWarning("Atomic settlement failed for match {Maker}/{Taker}", maker.Id, taker.Id);
                    break; // abort matching
                }

                // update filled quantities
                taker.FilledQuantity += matched;
                maker.FilledQuantity += matched;

                // build trade record
                var trade = new Trade
                {
                    Id = (int)await redis.StringIncrementAsync("global:trade_id"),
                    TradingPairId = taker.TradingPairId,
                    BuyOrderId = taker.Side == OrderSide.Buy ? taker.Id : maker.Id,
                    SellOrderId = taker.Side == OrderSide.Sell ? taker.Id : maker.Id,
                    Price = price,
                    Quantity = matched,
                    BuyerId = taker.Side == OrderSide.Buy ? taker.UserId ?? 0 : maker.UserId ?? 0,
                    SellerId = taker.Side == OrderSide.Sell ? taker.UserId ?? 0 : maker.UserId ?? 0,
                    ExecutedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                trades.Add(trade);

                // persist trade to redis (hash + list + sync queue)
                await SaveTradeToRedis(trade, symbol, redis);

                // update order status in redis
                var makerStatus = maker.FilledQuantity >= maker.Quantity ? OrderStatus.Filled : OrderStatus.PartiallyFilled;
                var takerStatus = taker.FilledQuantity >= taker.Quantity ? OrderStatus.Filled : (taker.FilledQuantity > 0 ? OrderStatus.PartiallyFilled : OrderStatus.Active);
                await redisOrders.UpdateOrderStatusAsync(maker.Id, makerStatus, maker.FilledQuantity);
                await redisOrders.UpdateOrderStatusAsync(taker.Id, takerStatus, taker.FilledQuantity);

                if (maker.FilledQuantity >= maker.Quantity)
                {
                    book.RemoveOrder(maker);
                }
            }

            return trades;
        }

        private async Task<bool> ExecuteTradeAssetsAtomicAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity, string symbol, IRedisCache redis)
        {
            // reuse the Lua script used in RedisOrderMatchingEngine
            const long PRECISION = 100_000_000;
            var (baseCurrency, quoteCurrency) = ParseSymbol(symbol);
            var baseAmount = quantity;
            var quoteAmount = quantity * price;

            var buyUserId = buyOrder.UserId ?? throw new InvalidOperationException("买单缺少用户ID");
            var sellUserId = sellOrder.UserId ?? throw new InvalidOperationException("卖单缺少用户ID");

            // use hash-tagged keys so they map to the same cluster slot when using Redis Cluster
            var buyQuoteKey = $"asset:{{{symbol}}}:{buyUserId}:{quoteCurrency}";
            var buyBaseKey = $"asset:{{{symbol}}}:{buyUserId}:{baseCurrency}";
            var sellBaseKey = $"asset:{{{symbol}}}:{sellUserId}:{baseCurrency}";
            var sellQuoteKey = $"asset:{{{symbol}}}:{sellUserId}:{quoteCurrency}";

            var quoteAmountLong = (long)(quoteAmount * PRECISION);
            var baseAmountLong = (long)(baseAmount * PRECISION);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var script = @"
            local buyQuoteFrozen = tonumber(redis.call('HGET', KEYS[1], 'frozenBalance') or 0)
            if buyQuoteFrozen < tonumber(ARGV[1]) then
                return 0
            end
            local sellBaseFrozen = tonumber(redis.call('HGET', KEYS[3], 'frozenBalance') or 0)
            if sellBaseFrozen < tonumber(ARGV[2]) then
                return 0
            end
            redis.call('HINCRBY', KEYS[1], 'frozenBalance', -ARGV[1])
            redis.call('HSET', KEYS[1], 'updatedAt', ARGV[3])
            redis.call('HINCRBY', KEYS[2], 'availableBalance', ARGV[2])
            redis.call('HSET', KEYS[2], 'updatedAt', ARGV[3])
            redis.call('HINCRBY', KEYS[3], 'frozenBalance', -ARGV[2])
            redis.call('HSET', KEYS[3], 'updatedAt', ARGV[3])
            redis.call('HINCRBY', KEYS[4], 'availableBalance', ARGV[1])
            redis.call('HSET', KEYS[4], 'updatedAt', ARGV[3])
            return 1
            ";

            try
            {
                // Use IRedisCache.Execute to run EVAL so MatchEngine doesn't depend on StackExchange.Redis types
                var res = redis.Execute("EVAL", script, 4, buyQuoteKey, buyBaseKey, sellBaseKey, sellQuoteKey, quoteAmountLong.ToString(), baseAmountLong.ToString(), timestamp.ToString());
                return res?.ToString() == "1";
            }
            catch (Exception ex)
            {
                // Log detailed context to aid diagnosis (CROSSSLOT / unknown command / NOSCRIPT etc.)
                try
                {
                    _logger.LogError(ex, "Redis error during atomic settlement. Message={Message} Keys=[{Keys}] Args=[{Args}] ScriptPreview={ScriptPreview}",
                        ex.Message,
                        string.Join(",", new[] { buyQuoteKey, buyBaseKey, sellBaseKey, sellQuoteKey }),
                        string.Join(",", new[] { quoteAmountLong.ToString(), baseAmountLong.ToString(), timestamp.ToString() }),
                        script.Length > 200 ? script.Substring(0, 200) + "..." : script);
                }
                catch { /* swallow logging errors */ }

                return false;
            }
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

        #region 内存订单簿
        private class OrderBook
        {
            public string Symbol { get; }
            public SemaphoreSlim Lock { get; } = new(1, 1);

            // bids: price desc, asks: price asc
            private readonly SortedDictionary<decimal, Queue<Order>> _bids = new(new DescComparer());
            private readonly SortedDictionary<decimal, Queue<Order>> _asks = new();

            public OrderBook(string symbol) { Symbol = symbol; }

            public void AddOrder(Order order)
            {
                var dict = order.Side == OrderSide.Buy ? _bids : _asks;
                var price = order.Price ?? 0m;
                if (!dict.TryGetValue(price, out var q))
                {
                    q = new Queue<Order>();
                    dict[price] = q;
                }
                q.Enqueue(order);
            }

            public Order GetBestOpposite(OrderSide side)
            {
                var dict = side == OrderSide.Buy ? _asks : _bids; // opposite side
                if (dict.Count == 0) return null;
                var first = dict.First();
                var q = first.Value;
                while (q.Count > 0)
                {
                    var order = q.Peek();
                    if (order.FilledQuantity >= order.Quantity)
                    {
                        q.Dequeue();
                        continue;
                    }
                    return order;
                }
                // empty level -> remove
                dict.Remove(first.Key);
                return GetBestOpposite(side);
            }

            public void RemoveOrder(Order order)
            {
                var dict = order.Side == OrderSide.Buy ? _bids : _asks;
                var price = order.Price ?? 0m;
                if (dict.TryGetValue(price, out var q))
                {
                    // linear remove (acceptable for small queues)
                    var list = q.ToList();
                    list.RemoveAll(o => o.Id == order.Id);
                    dict[price] = new Queue<Order>(list);
                    if (dict[price].Count == 0) dict.Remove(price);
                }
            }

            private class DescComparer : IComparer<decimal>
            {
                public int Compare(decimal x, decimal y) => y.CompareTo(x);
            }
        }
        #endregion

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
