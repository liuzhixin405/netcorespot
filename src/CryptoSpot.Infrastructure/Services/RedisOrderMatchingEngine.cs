using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace CryptoSpot.Infrastructure.Services;

/// <summary>
/// Redis-First 订单撮合引擎（所有操作在 Redis 中，零数据库访问）
/// </summary>
public class RedisOrderMatchingEngine
{
    private readonly RedisOrderRepository _redisOrders;
    private readonly RedisAssetRepository _redisAssets;
    private readonly IRedisCache _redis;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RedisOrderMatchingEngine> _logger;
    private readonly Dictionary<string, SemaphoreSlim> _symbolLocks = new();
    private const string TRADE_ID_KEY = "global:trade_id";

    public RedisOrderMatchingEngine(
        RedisOrderRepository redisOrders,
        RedisAssetRepository redisAssets,
        IRedisCache redis,
        IServiceProvider serviceProvider,
        ILogger<RedisOrderMatchingEngine> logger)
    {
        _redisOrders = redisOrders;
        _redisAssets = redisAssets;
        _redis = redis;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    #region 下单接口

    /// <summary>
    /// 下单（完全在 Redis 中执行）
    /// </summary>
    public async Task<Order> PlaceOrderAsync(Order order, string symbol)
    {
        var symbolLock = GetSymbolLock(symbol);

        await symbolLock.WaitAsync();
        try
        {
            _logger.LogInformation("📝 下单请求: UserId={UserId} {Symbol} {Side} {Type} {Price}x{Quantity}",
                order.UserId, symbol, order.Side, order.Type, order.Price, order.Quantity);

            // 1. 冻结资产
            var (currency, amount) = GetFreezeAmount(order, symbol);
            var userId = order.UserId ?? throw new InvalidOperationException("订单缺少用户ID");
            var freezeSuccess = await _redisAssets.FreezeAssetAsync(userId, currency, amount);

            if (!freezeSuccess)
            {
                throw new InvalidOperationException($"余额不足：需要 {amount} {currency}");
            }

            // 2. 创建订单（写入 Redis）
            order.Status = OrderStatus.Active;
            await _redisOrders.CreateOrderAsync(order, symbol);

            // 3. 立即撮合
            var trades = await MatchOrderAsync(order, symbol);

            _logger.LogInformation("Order placed: OrderId={OrderId}, Trades={TradeCount}",
                order.Id, trades.Count);

            return order;
        }
        finally
        {
            symbolLock.Release();
        }
    }

    /// <summary>
    /// 取消订单
    /// </summary>
    public async Task<bool> CancelOrderAsync(int orderId, int userId, string symbol)
    {
        var order = await _redisOrders.GetOrderByIdAsync(orderId);
        if (order == null || order.UserId != userId)
        {
            _logger.LogWarning("Order not found or unauthorized: OrderId={OrderId} UserId={UserId}", orderId, userId);
            return false;
        }

        if (order.Status != OrderStatus.Active && order.Status != OrderStatus.PartiallyFilled)
        {
            _logger.LogWarning("Order status does not allow cancellation: OrderId={OrderId} Status={Status}", orderId, order.Status);
            return false;
        }

        var symbolLock = GetSymbolLock(symbol);
        await symbolLock.WaitAsync();
        try
        {
            // 1. 更新订单状态
            await _redisOrders.UpdateOrderStatusAsync(orderId, OrderStatus.Cancelled, order.FilledQuantity);

            // 2. 解冻未成交资产
            var unfilledQuantity = order.Quantity - order.FilledQuantity;
            if (unfilledQuantity > 0)
            {
                var (currency, amount) = GetFreezeAmount(order, symbol);
                var unfreezeAmount = amount * (unfilledQuantity / order.Quantity);
                var userIdValue = order.UserId ?? throw new InvalidOperationException("订单缺少用户ID");
                await _redisAssets.UnfreezeAssetAsync(userIdValue, currency, unfreezeAmount);
            }

            _logger.LogInformation("Order cancelled: OrderId={OrderId}", orderId);

            // 3. 推送订单簿更新
            await PushOrderBookUpdate(symbol);

            return true;
        }
        finally
        {
            symbolLock.Release();
        }
    }

    #endregion

    #region 撮合逻辑

    /// <summary>
    /// 撮合单个订单
    /// </summary>
    private async Task<List<Trade>> MatchOrderAsync(Order order, string symbol)
    {
        var trades = new List<Trade>();

        if (order.Type == OrderType.Market)
        {
            // 市价单：按对手盘最优价格成交
            trades = await MatchMarketOrderAsync(order, symbol);
        }
        else
        {
            // 限价单：按价格优先、时间优先撮合
            trades = await MatchLimitOrderAsync(order, symbol);
        }

        // 更新订单状态
        if (order.FilledQuantity >= order.Quantity)
        {
            await _redisOrders.UpdateOrderStatusAsync(order.Id, OrderStatus.Filled, order.FilledQuantity);
        }
        else if (order.FilledQuantity > 0)
        {
            await _redisOrders.UpdateOrderStatusAsync(order.Id, OrderStatus.PartiallyFilled, order.FilledQuantity);
        }

        // 推送订单簿更新
        if (trades.Count > 0)
        {
            await PushOrderBookUpdate(symbol);
        }

        return trades;
    }

    /// <summary>
    /// 市价单撮合
    /// </summary>
    private async Task<List<Trade>> MatchMarketOrderAsync(Order order, string symbol)
    {
        var trades = new List<Trade>();
        var oppositeOrders = await GetOppositeOrders(order, symbol);

        foreach (var oppositeOrder in oppositeOrders)
        {
            if (order.FilledQuantity >= order.Quantity) break;

            var matchedQuantity = Math.Min(
                order.Quantity - order.FilledQuantity,
                oppositeOrder.Quantity - oppositeOrder.FilledQuantity);

            var trade = await ExecuteTrade(order, oppositeOrder, oppositeOrder.Price ?? 0, matchedQuantity, symbol);
            trades.Add(trade);

            order.FilledQuantity += matchedQuantity;
            oppositeOrder.FilledQuantity += matchedQuantity;

            // 更新对手订单状态
            var oppositeStatus = oppositeOrder.FilledQuantity >= oppositeOrder.Quantity
                ? OrderStatus.Filled
                : OrderStatus.PartiallyFilled;
            await _redisOrders.UpdateOrderStatusAsync(oppositeOrder.Id, oppositeStatus, oppositeOrder.FilledQuantity);
        }

        return trades;
    }

    /// <summary>
    /// 限价单撮合
    /// </summary>
    private async Task<List<Trade>> MatchLimitOrderAsync(Order order, string symbol)
    {
        var trades = new List<Trade>();
        var oppositeOrders = await GetOppositeOrders(order, symbol);

        foreach (var oppositeOrder in oppositeOrders)
        {
            // 限价单价格检查
            if (!CanMatch(order, oppositeOrder)) break;

            if (order.FilledQuantity >= order.Quantity) break;

            var matchedQuantity = Math.Min(
                order.Quantity - order.FilledQuantity,
                oppositeOrder.Quantity - oppositeOrder.FilledQuantity);

            // 使用对手盘价格成交（价格优先原则）
            var matchPrice = oppositeOrder.Price ?? 0;
            var trade = await ExecuteTrade(order, oppositeOrder, matchPrice, matchedQuantity, symbol);
            trades.Add(trade);

            order.FilledQuantity += matchedQuantity;
            oppositeOrder.FilledQuantity += matchedQuantity;

            // 更新对手订单状态
            var oppositeStatus = oppositeOrder.FilledQuantity >= oppositeOrder.Quantity
                ? OrderStatus.Filled
                : OrderStatus.PartiallyFilled;
            await _redisOrders.UpdateOrderStatusAsync(oppositeOrder.Id, oppositeStatus, oppositeOrder.FilledQuantity);
        }

        return trades;
    }

    #endregion

    #region 成交执行

    /// <summary>
    /// 执行成交（原子操作）
    /// </summary>
    private async Task<Trade> ExecuteTrade(Order buyOrder, Order sellOrder, decimal price, decimal quantity, string symbol)
    {
        // 确保买卖方向正确
        if (buyOrder.Side == OrderSide.Sell)
        {
            (buyOrder, sellOrder) = (sellOrder, buyOrder);
        }

        // 生成成交 ID
        var tradeId = (int)await _redis.StringIncrementAsync(TRADE_ID_KEY);

        // 解析交易对
        var (baseCurrency, quoteCurrency) = ParseSymbol(symbol);
        var baseAmount = quantity;
        var quoteAmount = quantity * price;

        // 买方：扣除冻结的计价货币，增加基础货币
        var buyUserId = buyOrder.UserId ?? throw new InvalidOperationException("买单缺少用户ID");
        var sellUserId = sellOrder.UserId ?? throw new InvalidOperationException("卖单缺少用户ID");
        
        // 🔒 使用 Lua 脚本保证 4 个资产操作的原子性（防止资金丢失）
        var success = await ExecuteTradeAssetsAtomicAsync(
            buyUserId, sellUserId, 
            baseCurrency, quoteCurrency, 
            baseAmount, quoteAmount);
        
        if (!success)
        {
            _logger.LogError("❌ 成交资产结算失败（原子性检查）: BuyUser={BuyUserId} SellUser={SellUserId}",
                buyUserId, sellUserId);
            throw new InvalidOperationException("成交资产结算失败，交易已回滚");
        }

        // 创建成交记录
        var trade = new Trade
        {
            Id = tradeId,
            TradingPairId = buyOrder.TradingPairId,
            BuyOrderId = buyOrder.Id,
            SellOrderId = sellOrder.Id,
            Price = price,
            Quantity = quantity,
            BuyerId = buyUserId,
            SellerId = sellUserId,
            ExecutedAt = DateTimeExtensions.GetCurrentUnixTimeMilliseconds()
        };

        // 保存成交记录到 Redis
        await SaveTradeToRedis(trade, symbol);

        // 推送成交数据到 SignalR
        await PushTradeToUsersAsync(buyUserId, sellUserId, trade, symbol);

        _logger.LogInformation("💰 成交: TradeId={TradeId} {Symbol} {Price}x{Quantity}, 买方={BuyUserId}, 卖方={SellUserId}",
            tradeId, symbol, price, quantity, buyUserId, sellUserId);

        return trade;
    }

    private async Task SaveTradeToRedis(Trade trade, string symbol)
    {
        var key = $"trade:{trade.Id}";
        await _redis.HMSetAsync(key,
            "id", trade.Id.ToString(),
            "tradingPairId", trade.TradingPairId.ToString(),
            "buyOrderId", trade.BuyOrderId.ToString(),
            "sellOrderId", trade.SellOrderId.ToString(),
            "price", trade.Price.ToString("F8"),
            "quantity", trade.Quantity.ToString("F8"),
            "buyerId", trade.BuyerId.ToString(),
            "sellerId", trade.SellerId.ToString(),
            "executedAt", trade.ExecutedAt.ToString());

        // 添加到成交历史列表（最多保留 1000 条）
        await _redis.ListLeftPushAsync($"trades:{symbol}", trade.Id.ToString());
        await _redis.LTrimAsync($"trades:{symbol}", 0, 999);

        // 加入同步队列
        var json = JsonSerializer.Serialize(new
        {
            tradeId = trade.Id,
            operation = "CREATE",
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        await _redis.ListLeftPushAsync("sync_queue:trades", json);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 🔒 原子性执行成交资产结算（使用 Lua 脚本保证 4 个操作的原子性）
    /// </summary>
    private async Task<bool> ExecuteTradeAssetsAtomicAsync(
        int buyUserId, int sellUserId,
        string baseCurrency, string quoteCurrency,
        decimal baseAmount, decimal quoteAmount)
    {
        const long PRECISION = 100_000_000; // 8 位小数精度
        
        var buyQuoteKey = $"asset:{buyUserId}:{quoteCurrency}";   // 买方计价货币资产
        var buyBaseKey = $"asset:{buyUserId}:{baseCurrency}";     // 买方基础货币资产
        var sellBaseKey = $"asset:{sellUserId}:{baseCurrency}";   // 卖方基础货币资产
        var sellQuoteKey = $"asset:{sellUserId}:{quoteCurrency}"; // 卖方计价货币资产
        
        var quoteAmountLong = (long)(quoteAmount * PRECISION);
        var baseAmountLong = (long)(baseAmount * PRECISION);
        var timestamp = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();

        // Lua 脚本：原子性执行 4 个资产操作
        var script = @"
            -- 1. 检查买方冻结的计价货币是否足够
            local buyQuoteFrozen = tonumber(redis.call('HGET', KEYS[1], 'frozenBalance') or 0)
            if buyQuoteFrozen < tonumber(ARGV[1]) then
                return 0  -- 余额不足
            end
            
            -- 2. 检查卖方冻结的基础货币是否足够
            local sellBaseFrozen = tonumber(redis.call('HGET', KEYS[3], 'frozenBalance') or 0)
            if sellBaseFrozen < tonumber(ARGV[2]) then
                return 0  -- 余额不足
            end
            
            -- 3. 买方：扣除冻结的计价货币，增加可用的基础货币
            redis.call('HINCRBY', KEYS[1], 'frozenBalance', -ARGV[1])
            redis.call('HSET', KEYS[1], 'updatedAt', ARGV[3])
            
            redis.call('HINCRBY', KEYS[2], 'availableBalance', ARGV[2])
            redis.call('HSET', KEYS[2], 'updatedAt', ARGV[3])
            
            -- 4. 卖方：扣除冻结的基础货币，增加可用的计价货币
            redis.call('HINCRBY', KEYS[3], 'frozenBalance', -ARGV[2])
            redis.call('HSET', KEYS[3], 'updatedAt', ARGV[3])
            
            redis.call('HINCRBY', KEYS[4], 'availableBalance', ARGV[1])
            redis.call('HSET', KEYS[4], 'updatedAt', ARGV[3])
            
            return 1  -- 成功
        ";

        try
        {
            var db = _redis.Connection.GetDatabase();
            var result = await db.ScriptEvaluateAsync(script,
                new StackExchange.Redis.RedisKey[] 
                { 
                    buyQuoteKey,  // KEYS[1]: 买方计价货币
                    buyBaseKey,   // KEYS[2]: 买方基础货币
                    sellBaseKey,  // KEYS[3]: 卖方基础货币
                    sellQuoteKey  // KEYS[4]: 卖方计价货币
                },
                new StackExchange.Redis.RedisValue[] 
                { 
                    quoteAmountLong,  // ARGV[1]: 计价货币数量
                    baseAmountLong,   // ARGV[2]: 基础货币数量
                    timestamp         // ARGV[3]: 时间戳
                });

            var success = result.ToString() == "1";

            if (success)
            {
                // 加入同步队列（确保 MySQL 同步）
                await EnqueueAssetSyncBatch(buyUserId, sellUserId, baseCurrency, quoteCurrency);
                
                _logger.LogDebug("✅ 成交资产结算成功（原子性）: BuyUser={BuyUserId} SellUser={SellUserId} Base={BaseAmount} Quote={QuoteAmount}",
                    buyUserId, sellUserId, baseAmount, quoteAmount);
            }
            else
            {
                _logger.LogWarning("⚠️ 成交资产结算失败（余额不足）: BuyUser={BuyUserId} SellUser={SellUserId}",
                    buyUserId, sellUserId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 成交资产结算异常: BuyUser={BuyUserId} SellUser={SellUserId}",
                buyUserId, sellUserId);
            return false;
        }
    }

    /// <summary>
    /// 将成交涉及的 4 个资产加入同步队列
    /// </summary>
    private async Task EnqueueAssetSyncBatch(int buyUserId, int sellUserId, string baseCurrency, string quoteCurrency)
    {
        var db = _redis.Connection.GetDatabase();
        var timestamp = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();

        var assets = new[]
        {
            new { userId = buyUserId, symbol = quoteCurrency },
            new { userId = buyUserId, symbol = baseCurrency },
            new { userId = sellUserId, symbol = baseCurrency },
            new { userId = sellUserId, symbol = quoteCurrency }
        };

        foreach (var asset in assets)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                userId = asset.userId,
                symbol = asset.symbol,
                timestamp
            });

            await db.ListRightPushAsync("sync_queue:assets", json);
        }
    }

    private SemaphoreSlim GetSymbolLock(string symbol)
    {
        lock (_symbolLocks)
        {
            if (!_symbolLocks.ContainsKey(symbol))
            {
                _symbolLocks[symbol] = new SemaphoreSlim(1, 1);
            }
            return _symbolLocks[symbol];
        }
    }

    private async Task<List<Order>> GetOppositeOrders(Order order, string symbol)
    {
        var oppositeSide = order.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
        return await _redisOrders.GetActiveOrdersAsync(symbol, oppositeSide, 100);
    }

    private bool CanMatch(Order order1, Order order2)
    {
        // 不能自己和自己成交
        if (order1.UserId == order2.UserId) return false;

        // 买卖方向必须相反
        if (order1.Side == order2.Side) return false;

        // 价格检查：买单价格 >= 卖单价格
        var price1 = order1.Price ?? 0;
        var price2 = order2.Price ?? 0;
        
        if (order1.Side == OrderSide.Buy)
        {
            return price1 >= price2;
        }
        else
        {
            return price2 >= price1;
        }
    }

    private (string currency, decimal amount) GetFreezeAmount(Order order, string symbol)
    {
        var (baseCurrency, quoteCurrency) = ParseSymbol(symbol);
        var price = order.Price ?? 0;

        if (order.Side == OrderSide.Buy)
        {
            // 买单：冻结计价货币（USDT）
            return (quoteCurrency, order.Quantity * price);
        }
        else
        {
            // 卖单：冻结基础货币（BTC）
            return (baseCurrency, order.Quantity);
        }
    }

    private (string baseCurrency, string quoteCurrency) ParseSymbol(string symbol)
    {
        // BTCUSDT -> (BTC, USDT)
        // ETHUSDT -> (ETH, USDT)
        // SOLUSDT -> (SOL, USDT)
        var quoteCurrency = "USDT"; // 目前只支持 USDT
        var baseCurrency = symbol.Replace(quoteCurrency, "");
        return (baseCurrency, quoteCurrency);
    }

    /// <summary>
    /// 推送订单簿更新 (使用 Scoped Service)
    /// </summary>
    private async Task PushOrderBookUpdate(string symbol)
    {
        try
        {
            var (bids, asks) = await _redisOrders.GetOrderBookDepthAsync(symbol, 20);

            var bidDtos = bids.Select(x => new Application.DTOs.Trading.OrderBookLevelDto
            {
                Price = x.price,
                Quantity = x.quantity
            }).ToList();

            var askDtos = asks.Select(x => new Application.DTOs.Trading.OrderBookLevelDto
            {
                Price = x.price,
                Quantity = x.quantity
            }).ToList();

            using (var scope = _serviceProvider.CreateScope())
            {
                var realTimePush = scope.ServiceProvider.GetService<IRealTimeDataPushService>();
                if (realTimePush != null)
                {
                    await realTimePush.PushExternalOrderBookSnapshotAsync(symbol, bidDtos, askDtos, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push order book: {Symbol}", symbol);
        }
    }

    /// <summary>
    /// 推送成交记录到买卖双方用户 (使用 Scoped Service)
    /// </summary>
    private async Task PushTradeToUsersAsync(int buyUserId, int sellUserId, Trade trade, string symbol)
    {
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var realTimePush = scope.ServiceProvider.GetService<IRealTimeDataPushService>();
                if (realTimePush != null)
                {
                    var tradeDto = new TradeDto
                    {
                        Id = trade.Id,
                        Symbol = symbol,
                        Price = trade.Price,
                        Quantity = trade.Quantity,
                        BuyOrderId = trade.BuyOrderId,
                        SellOrderId = trade.SellOrderId,
                        BuyerId = buyUserId,
                        SellerId = sellUserId,
                        ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(trade.ExecutedAt).DateTime,
                        TotalValue = trade.Price * trade.Quantity
                    };

                    // 推送给买方
                    await realTimePush.PushUserTradeAsync(buyUserId, tradeDto);
                    // 推送给卖方
                    await realTimePush.PushUserTradeAsync(sellUserId, tradeDto);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push trade: TradeId={TradeId}", trade.Id);
        }
    }

    #endregion
}
