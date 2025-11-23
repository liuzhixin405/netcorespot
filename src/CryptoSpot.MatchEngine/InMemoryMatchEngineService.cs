using System.Collections.Concurrent;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Redis;
using CryptoSpot.MatchEngine.Core;
using CryptoSpot.MatchEngine.Commands;
using CryptoSpot.MatchEngine.Services;
using CryptoSpot.Bus.Core;
using CryptoSpot.Persistence.Redis.Repositories;


namespace CryptoSpot.MatchEngine
{
    /// <summary>
    /// 内存撮合引擎 - 价格时间优先算法
    /// - Redis Lua 脚本保证资产结算原子性
    /// - 使用统一 CommandBus 发布事件
    /// - 每个交易对独立锁，确保并发安全
    /// </summary>
    public class InMemoryMatchEngineService : IMatchEngineService
    {
        private readonly ILogger<InMemoryMatchEngineService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, IOrderBook> _orderBooks = new();
        private readonly ISettlementService _settlement;
        private readonly ICommandBus _commandBus;
        private readonly IMatchingAlgorithm _matchingAlgorithm;
        private readonly IMatchEngineMetrics _metrics;
        private readonly IOrderBookSnapshotService _snapshotService;
        private readonly ITradingPairParser _pairParser;

        public InMemoryMatchEngineService(
            ILogger<InMemoryMatchEngineService> logger, 
            IServiceProvider serviceProvider, 
            ISettlementService settlement, 
            ICommandBus commandBus,
            IMatchingAlgorithm matchingAlgorithm, 
            IMatchEngineMetrics metrics,
            IOrderBookSnapshotService snapshotService,
            ITradingPairParser pairParser)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _settlement = settlement;
            _commandBus = commandBus;
            _matchingAlgorithm = matchingAlgorithm;
            _metrics = metrics;
            _snapshotService = snapshotService;
            _pairParser = pairParser;
        }

        /// <summary>
        /// 下单并撮合
        /// </summary>
        public async Task<Order> PlaceOrderAsync(Order order, string symbol)
        {
            ValidateOrderInput(order, symbol);

            var orderBook = GetOrCreateOrderBook(symbol);
            var semaphore = GetOrderBookLock(orderBook);
            
            await semaphore.WaitAsync();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = CreateMatchingContext(scope, symbol);

                // 1. 冻结资产
                await FreezeUserAssetsAsync(order, symbol, context);

                // 2. 添加到订单簿并持久化
                order.Status = OrderStatus.Active;
                orderBook.Add(order);
                await context.OrderRepository.CreateOrderAsync(order, symbol);

                // 3. 执行撮合
                var trades = await ExecuteMatchingAsync(order, orderBook, symbol, context);

                // 4. 未成交则推送订单簿更新
                if (trades.Count == 0)
                {
                    await PublishOrderPlacedEventsAsync(symbol, order);
                }

                return order;
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// 验证订单输入
        /// </summary>
        private static void ValidateOrderInput(Order order, string symbol)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (string.IsNullOrEmpty(symbol)) throw new ArgumentNullException(nameof(symbol));
            if (order.UserId == null) throw new InvalidOperationException("订单缺少用户ID");
        }

        /// <summary>
        /// 获取或创建订单簿
        /// </summary>
        private IOrderBook GetOrCreateOrderBook(string symbol)
        {
            return _orderBooks.GetOrAdd(symbol, s => new InMemoryOrderBook(s));
        }

        /// <summary>
        /// 获取订单簿锁
        /// </summary>
        private static SemaphoreSlim GetOrderBookLock(IOrderBook orderBook)
        {
            return (SemaphoreSlim)((InMemoryOrderBook)orderBook).SyncRoot;
        }

        /// <summary>
        /// 创建撮合上下文
        /// </summary>
        private MatchingContext CreateMatchingContext(IServiceScope scope, string symbol)
        {
            return new MatchingContext(
                scope.ServiceProvider.GetRequiredService<RedisAssetRepository>(),
                scope.ServiceProvider.GetRequiredService<RedisOrderRepository>(),
                scope.ServiceProvider.GetRequiredService<IRedisCache>(),
                symbol
            );
        }

        /// <summary>
        /// 冻结用户资产
        /// </summary>
        private async Task FreezeUserAssetsAsync(Order order, string symbol, MatchingContext context)
        {
            var (currency, amount) = _pairParser.GetFreezeAmount(order, symbol);
            var userId = order.UserId!.Value;
            
            // symbol 作为 tag 确保 Redis Cluster 中的相关键在同一槽位
            var freezeSuccess = await context.AssetRepository.FreezeAssetAsync(
                userId, currency, amount, symbol);
            
            if (!freezeSuccess)
            {
                throw new InvalidOperationException($"余额不足：需要 {amount} {currency}");
            }
        }

        /// <summary>
        /// 发布订单下单事件（未成交）
        /// </summary>
        private async Task PublishOrderPlacedEventsAsync(string symbol, Order order)
        {
            try
            {
                await _snapshotService.PushSnapshotAsync(symbol);
                
                // Fire-and-forget，不阻塞撮合
                _ = _commandBus.SendAsync<OrderPlacedCommand, bool>(
                    new OrderPlacedCommand(symbol, order) { Symbol = symbol });
                _ = _commandBus.SendAsync<OrderBookChangedCommand, bool>(
                    new OrderBookChangedCommand(symbol));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "推送订单簿更新失败: Symbol={Symbol}, OrderId={OrderId}", 
                    symbol, order.Id);
            }
        }

        /// <summary>
        /// 执行撮合
        /// </summary>
        private async Task<List<Trade>> ExecuteMatchingAsync(
            Order taker, 
            IOrderBook orderBook, 
            string symbol, 
            MatchingContext context)
        {
            var trades = new List<Trade>();
            
            foreach (var matchSlice in _matchingAlgorithm.Match(orderBook, taker))
            {
                // 1. 记录撮合尝试指标
                _metrics.ObserveMatchAttempt(symbol, matchSlice.Quantity, matchSlice.Price);

                // 2. 执行资产结算（原子操作）
                var settlementResult = await SettleTradeAsync(matchSlice, symbol);
                if (!settlementResult.Success)
                {
                    _logger.LogWarning(
                        "结算失败，终止剩余撮合: Symbol={Symbol}, Maker={MakerId}, Taker={TakerId}", 
                        symbol, matchSlice.Maker.Id, matchSlice.Taker.Id);
                    break;
                }

                // 3. 创建交易记录
                var trade = await CreateTradeRecordAsync(matchSlice, taker, symbol, context.Cache);
                trades.Add(trade);

                // 4. 持久化交易并更新订单状态
                await SaveTradeAsync(trade, symbol, context);
                await UpdateOrderStatusesAsync(matchSlice, taker, context.OrderRepository);

                // 5. 发布交易事件
                await PublishTradeEventsAsync(symbol, trade, matchSlice.Maker, taker);

                // 6. 吃单完全成交则终止
                if (taker.FilledQuantity >= taker.Quantity) break;
            }
            
            return trades;
        }

        /// <summary>
        /// 执行资产结算
        /// </summary>
        private async Task<SettlementResult> SettleTradeAsync(MatchSlice slice, string symbol)
        {
            var startTime = DateTime.UtcNow;
            var settlementContext = new SettlementContext(
                slice.Maker, 
                slice.Taker, 
                slice.Price, 
                slice.Quantity, 
                symbol);
            
            var result = await _settlement.SettleAsync(settlementContext);
            
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _metrics.ObserveSettlement(symbol, result.Success, duration);
            
            return result;
        }

        /// <summary>
        /// 创建交易记录
        /// </summary>
        private async Task<Trade> CreateTradeRecordAsync(
            MatchSlice slice, 
            Order taker, 
            string symbol, 
            IRedisCache cache)
        {
            var tradeId = await cache.StringIncrementAsync("global:trade_id");
            var isTakerBuy = taker.Side == OrderSide.Buy;
            
            return new Trade
            {
                Id = tradeId,
                TradingPairId = taker.TradingPairId,
                BuyOrderId = isTakerBuy ? taker.Id : slice.Maker.Id,
                SellOrderId = isTakerBuy ? slice.Maker.Id : taker.Id,
                Price = slice.Price,
                Quantity = slice.Quantity,
                BuyerId = isTakerBuy ? taker.UserId ?? 0 : slice.Maker.UserId ?? 0,
                SellerId = isTakerBuy ? slice.Maker.UserId ?? 0 : taker.UserId ?? 0,
                ExecutedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        /// <summary>
        /// 更新订单状态
        /// </summary>
        private static async Task UpdateOrderStatusesAsync(
            MatchSlice slice, 
            Order taker, 
            RedisOrderRepository orderRepository)
        {
            var makerStatus = CalculateOrderStatus(slice.Maker);
            var takerStatus = CalculateOrderStatus(taker);
            
            await orderRepository.UpdateOrderStatusAsync(
                slice.Maker.Id, makerStatus, slice.Maker.FilledQuantity);
            await orderRepository.UpdateOrderStatusAsync(
                taker.Id, takerStatus, taker.FilledQuantity);
        }

        /// <summary>
        /// 计算订单状态
        /// </summary>
        private static OrderStatus CalculateOrderStatus(Order order)
        {
            if (order.FilledQuantity >= order.Quantity)
                return OrderStatus.Filled;
            
            return order.FilledQuantity > 0 
                ? OrderStatus.PartiallyFilled 
                : OrderStatus.Active;
        }

        /// <summary>
        /// 发布交易执行事件
        /// </summary>
        private async Task PublishTradeEventsAsync(
            string symbol, 
            Trade trade, 
            Order maker, 
            Order taker)
        {
            try
            {
                // Fire-and-forget
                _ = _commandBus.SendAsync<TradeExecutedCommand, bool>(
                    new TradeExecutedCommand(symbol, trade, maker, taker) { Symbol = symbol });
                _ = _commandBus.SendAsync<OrderBookChangedCommand, bool>(
                    new OrderBookChangedCommand(symbol));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "发布交易事件失败: Symbol={Symbol}, TradeId={TradeId}", 
                    symbol, trade.Id);
            }
        }


        /// <summary>
        /// 保存交易（持久化 + 同步队列 + 实时推送）
        /// </summary>
        private async Task SaveTradeAsync(Trade trade, string symbol, MatchingContext context)
        {
            // 1. 持久化交易记录
            await PersistTradeToRedisAsync(trade, symbol, context.Cache);
            
            // 2. 添加到同步队列
            await EnqueueTradeSyncAsync(trade, context.Cache);
            
            // 3. 实时推送给用户
            await PushTradeToUsersAsync(trade, symbol);
        }

        /// <summary>
        /// 持久化交易到 Redis
        /// </summary>
        private static async Task PersistTradeToRedisAsync(Trade trade, string symbol, IRedisCache cache)
        {
            // Hash 存储交易详情
            var key = $"trade:{trade.Id}";
            await cache.HMSetAsync(key,
                "id", trade.Id.ToString(),
                "tradingPairId", trade.TradingPairId.ToString(),
                "buyOrderId", trade.BuyOrderId.ToString(),
                "sellOrderId", trade.SellOrderId.ToString(),
                "price", trade.Price.ToString("F8"),
                "quantity", trade.Quantity.ToString("F8"),
                "buyerId", trade.BuyerId.ToString(),
                "sellerId", trade.SellerId.ToString(),
                "executedAt", trade.ExecutedAt.ToString());

            // List 存储交易对历史（保留最近 1000 条）
            await cache.ListLeftPushAsync($"trades:{symbol}", trade.Id.ToString());
            await cache.LTrimAsync($"trades:{symbol}", 0, 999);
        }

        /// <summary>
        /// 将交易加入同步队列
        /// </summary>
        private static async Task EnqueueTradeSyncAsync(Trade trade, IRedisCache cache)
        {
            var syncMessage = System.Text.Json.JsonSerializer.Serialize(new
            {
                tradeId = trade.Id,
                operation = "CREATE",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            
            await cache.ListLeftPushAsync("sync_queue:trades", syncMessage);
        }

        /// <summary>
        /// 实时推送交易给买卖双方
        /// </summary>
        private async Task PushTradeToUsersAsync(Trade trade, string symbol)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var realTimePush = scope.ServiceProvider
                    .GetService<CryptoSpot.Application.Abstractions.Services.RealTime.IRealTimeDataPushService>();
                
                if (realTimePush == null) return;

                var tradeDto = CreateTradeDto(trade, symbol);
                await realTimePush.PushUserTradeAsync(trade.BuyerId, tradeDto);
                await realTimePush.PushUserTradeAsync(trade.SellerId, tradeDto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "实时推送交易失败: TradeId={TradeId}, Symbol={Symbol}", 
                    trade.Id, symbol);
            }
        }

        /// <summary>
        /// 创建交易 DTO
        /// </summary>
        private static CryptoSpot.Application.DTOs.Trading.TradeDto CreateTradeDto(Trade trade, string symbol)
        {
            return new CryptoSpot.Application.DTOs.Trading.TradeDto
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
        }
    }

    /// <summary>
    /// 撮合上下文 - 封装撮合过程需要的依赖
    /// </summary>
    internal sealed class MatchingContext
    {
        public RedisAssetRepository AssetRepository { get; }
        public RedisOrderRepository OrderRepository { get; }
        public IRedisCache Cache { get; }
        public string Symbol { get; }

        public MatchingContext(
            RedisAssetRepository assetRepository,
            RedisOrderRepository orderRepository,
            IRedisCache cache,
            string symbol)
        {
            AssetRepository = assetRepository;
            OrderRepository = orderRepository;
            Cache = cache;
            Symbol = symbol;
        }
    }
}
