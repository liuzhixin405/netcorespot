using CryptoSpot.Redis;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using System.Linq;
using DomainOrder = CryptoSpot.Domain.Entities.Order;
using StackExchange.Redis;

namespace CryptoSpot.Persistence.Redis.Repositories;

/// <summary>
/// Redis 订单仓储（运行时所有订单操作都在 Redis 中）
/// </summary>
public class RedisOrderRepository
{
    private readonly IRedisCache _redis;
    private readonly ILogger<RedisOrderRepository> _logger;
    private const string ORDER_ID_KEY = "global:order_id";

    public RedisOrderRepository(IRedisCache redis, ILogger<RedisOrderRepository> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    #region ID 生成

    /// <summary>
    /// 生成全局唯一订单 ID
    /// </summary>
    public async Task<int> GenerateOrderIdAsync()
    {
        return (int)await _redis.StringIncrementAsync(ORDER_ID_KEY);
    }

    #endregion

    #region 底层 Redis 访问
    // 底层访问通过 IRedisCache 封装提供，不再直接暴露 IDatabase
    #endregion

    #region 订单创建

    /// <summary>
    /// 创建订单（仅写 Redis，异步同步到 MySQL）
    /// 注意：调用前需要先设置 order.TradingPair 导航属性
    /// </summary>
    public async Task<DomainOrder> CreateOrderAsync(DomainOrder order, string symbol)
    {
        order.Id = await GenerateOrderIdAsync();
        order.CreatedAt = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();
        order.UpdatedAt = order.CreatedAt;

        // 保存订单完整数据
        await SaveOrderToRedisAsync(order, symbol);

        // 添加到用户订单索引
        try
        {
            _redis.Execute("SADD", $"user_orders:{order.UserId}", order.Id.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to SADD user_orders for user {UserId}", order.UserId);
        }

        // 如果是活跃订单，添加到订单簿
        if (order.Status == OrderStatus.Active || order.Status == OrderStatus.Pending || order.Status == OrderStatus.PartiallyFilled)
        {
            await AddToActiveOrderBook(order, symbol);

            // 发布到 Redis Stream 'orders:stream'，供 consumer-group 消费者（撮合/推送）使用
            try
            {
                // 使用 IRedisCache 的 XAddAsync，以便在不同实现间保持一致
                var fields = new (string, string)[]
                {
                    ("type", "order_created"),
                    ("symbol", symbol),
                    ("orderId", order.Id.ToString()),
                    ("side", order.Side.ToString()),
                    ("price", order.Price?.ToString() ?? "0"),
                    ("quantity", order.Quantity.ToString())
                };

                // key 名称使用 orders:stream
                await _redis.XAddAsync("orders:stream", "*", null, fields);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to XADD order_created to orders:stream for OrderId={OrderId}", order.Id);
            }
        }

        // 加入 MySQL 同步队列
        await EnqueueSyncOperation("orders", new
        {
            orderId = order.Id,
            operation = "CREATE",
            timestamp = DateTimeExtensions.GetCurrentUnixTimeMilliseconds()
        });

        _logger.LogDebug("✅ Redis 创建订单: {OrderId} {Symbol} {Side} {Price}x{Quantity}",
            order.Id, symbol, order.Side, order.Price, order.Quantity);

        return order;
    }

    /// <summary>
    /// 将已存在于 DB 的订单写入到 Redis（用于 DB 回退后回填缓存，best-effort）
    /// 不会修改 order.Id，只会把 order 的当前数据写到 Redis，并将同步操作加入队列
    /// </summary>
    public async Task SeedOrderAsync(DomainOrder order, string symbol)
    {
        // 不要覆盖创建时间/更新时间，由调用方保证已存在这些字段
        await SaveOrderToRedisAsync(order, symbol);

        // 添加到用户订单索引
        try
        {
            _redis.Execute("SADD", $"user_orders:{order.UserId}", order.Id.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to SADD user_orders for user {UserId}", order.UserId);
        }

        // 如果是活跃订单，添加到订单簿
        if (order.Status == OrderStatus.Active || order.Status == OrderStatus.Pending || order.Status == OrderStatus.PartiallyFilled)
        {
            await AddToActiveOrderBook(order, symbol);
        }

        // 加入 MySQL 同步队列（标记为 CREATE，Sync handler 应能幂等处理）
        await EnqueueSyncOperation("orders", new
        {
            orderId = order.Id,
            operation = "CREATE",
            timestamp = DateTimeExtensions.GetCurrentUnixTimeMilliseconds()
        });

        _logger.LogDebug("🔁 Backfilled DB order to Redis: {OrderId} {Symbol}", order.Id, symbol);
    }

        private async Task SaveOrderToRedisAsync(DomainOrder order, string symbol)
    {
        var key = $"order:{order.Id}";
        var hashEntries = new HashEntry[]
        {
            new HashEntry("orderId", order.OrderId ?? string.Empty),
            new HashEntry("clientOrderId", order.ClientOrderId ?? string.Empty),
            new HashEntry("id", order.Id.ToString()),
            new HashEntry("userId", order.UserId?.ToString() ?? ""),
            new HashEntry("tradingPairId", order.TradingPairId.ToString()),
            new HashEntry("symbol", symbol),
            new HashEntry("side", ((int)order.Side).ToString()),
            new HashEntry("type", ((int)order.Type).ToString()),
            new HashEntry("price", order.Price?.ToString() ?? "0"),
            new HashEntry("quantity", order.Quantity.ToString()),
            new HashEntry("filledQuantity", order.FilledQuantity.ToString()),
            new HashEntry("status", ((int)order.Status).ToString()),
            new HashEntry("createdAt", order.CreatedAt.ToString()),
            new HashEntry("updatedAt", order.UpdatedAt.ToString())
        };
        await _redis.HMSetAsync(key, hashEntries);
    }

    #endregion

    #region 订单查询

    /// <summary>
    /// 根据 ID 获取订单
    /// </summary>
    public async Task<DomainOrder?> GetOrderByIdAsync(long orderId)
    {
        var key = $"order:{orderId}";
        var exists = await _redis.ExistsAsync(key);
        if (!exists) return null;

        var data = await _redis.HGetAllAsync(key);
        return MapToOrder(data);
    }

    /// <summary>
    /// 获取用户的所有订单
    /// </summary>
        public async Task<List<DomainOrder>> GetUserOrdersAsync(long userId, int limit = 100)
    {
        var orders = new List<DomainOrder>();
        try
        {
                var members = _redis.Execute("SMEMBERS", $"user_orders:{userId}");
                if (members != null)
                {
                    // Normalize to string and split - Execute returns a RedisResult which may contain multi-bulk data.
                    var s = members.ToString() ?? string.Empty;
                    var parts = s.Split(new[] { '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(limit);
                    foreach (var part in parts)
                    {
                        if (long.TryParse(part, out var orderId))
                        {
                            var order = await GetOrderByIdAsync(orderId);
                            if (order != null) orders.Add(order);
                        }
                    }
                }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read user_orders for user {UserId}", userId);
        }

        return orders.OrderByDescending(o => o.CreatedAt).ToList();
    }

    /// <summary>
    /// 获取活跃订单（从 Sorted Set 中查询）
    /// </summary>
    public async Task<List<DomainOrder>> GetActiveOrdersAsync(string symbol, OrderSide side, int limit = 100)
    {
        var key = $"orders:active:{symbol}:{side}";

        // 买单按价格降序（高价优先），卖单按价格升序（低价优先）
        var orderIds = side == OrderSide.Buy
            ? await _redis.SortedSetRangeByScoreAsync(key, double.NegativeInfinity, double.PositiveInfinity, 
                isAscending: false, skip: 0, take: limit)
            : await _redis.SortedSetRangeByScoreAsync(key, double.NegativeInfinity, double.PositiveInfinity,
                isAscending: true, skip: 0, take: limit);

        var orders = new List<DomainOrder>();
        foreach (var orderIdStr in orderIds ?? new List<string>())
        {
            if (int.TryParse(orderIdStr, out var orderId))
            {
                var order = await GetOrderByIdAsync(orderId);
                // 只返回活跃状态的订单
                if (order != null && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Filled)
                {
                    orders.Add(order);
                }
            }
        }

        return orders;
    }

    #endregion

    #region 订单更新

    /// <summary>
    /// 更新订单状态
    /// </summary>
    public async Task UpdateOrderStatusAsync(long orderId, OrderStatus newStatus, decimal filledQuantity)
    {
        var order = await GetOrderByIdAsync(orderId);
        if (order == null)
        {
            throw new InvalidOperationException($"订单 {orderId} 不存在");
        }

        order.Status = newStatus;
        order.FilledQuantity = filledQuantity;
        order.UpdatedAt = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();

        // 更新 Redis
        try
        {
            await _redis.HMSetAsync($"order:{orderId}", "status", ((int)newStatus).ToString(), "filledQuantity", filledQuantity.ToString(), "updatedAt", order.UpdatedAt.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to HMSET order:{OrderId}", orderId);
        }

        // 如果订单完成或取消，从订单簿移除
        if (newStatus == OrderStatus.Filled || newStatus == OrderStatus.Cancelled)
        {
            var symbol = await _redis.HGetAsync($"order:{orderId}", "symbol");
            await RemoveFromActiveOrderBook(orderId, symbol, order.Side);
        }

        // 加入同步队列
        await EnqueueSyncOperation("orders", new
        {
            orderId = order.Id,
            operation = "UPDATE",
            timestamp = DateTimeExtensions.GetCurrentUnixTimeMilliseconds()
        });
    }

    #endregion

    #region 订单取消

    /// <summary>
    /// 取消订单
    /// </summary>
    public async Task<bool> CancelOrderAsync(long orderId, long userId)
    {
        var order = await GetOrderByIdAsync(orderId);
        if (order == null || order.UserId != userId)
        {
            return false;
        }

        // 只能取消活跃、部分成交或待处理的订单
        if (order.Status != OrderStatus.Active && order.Status != OrderStatus.PartiallyFilled && order.Status != OrderStatus.Pending)
        {
            return false;
        }

        await UpdateOrderStatusAsync(orderId, OrderStatus.Cancelled, order.FilledQuantity);
        return true;
    }

    #endregion

    #region 订单簿管理

    /// <summary>
    /// 添加到活跃订单簿（Sorted Set，按价格排序）
    /// </summary>
    private async Task AddToActiveOrderBook(DomainOrder order, string symbol)
    {
        var key = $"orders:active:{symbol}:{order.Side}";

        // 价格为 score（用于排序）
        var score = (double)(order.Price ?? 0);
        try
        {
            await _redis.SortedSetAddAsync(key, new System.Collections.Generic.Dictionary<string, long> { { order.Id.ToString(), (long)score } });
        }
        catch (Exception ex)
        {
            // Fallback to raw ZADD
            try { _redis.Execute("ZADD", key, score.ToString(), order.Id.ToString()); } catch { _logger.LogWarning(ex, "Failed to add to sorted set {Key}", key); }
        }

        _logger.LogDebug("📖 加入订单簿: {Symbol} {Side} Price={Price} OrderId={OrderId}",
            symbol, order.Side, order.Price, order.Id);
    }

    /// <summary>
    /// 从活跃订单簿移除
    /// </summary>
    private async Task RemoveFromActiveOrderBook(long orderId, string symbol, OrderSide side)
    {
        var key = $"orders:active:{symbol}:{side}";
        try
        {
            _redis.Execute("ZREM", key, orderId.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ZREM {OrderId} from {Key}", orderId, key);
        }

        _logger.LogDebug("📕 移出订单簿: {Symbol} {Side} OrderId={OrderId}",
            symbol, side, orderId);
    }

    #endregion

    #region 订单簿深度查询

    /// <summary>
    /// 获取订单簿深度（聚合相同价格的订单）
    /// </summary>
    public async Task<(List<(decimal price, decimal quantity)> bids, List<(decimal price, decimal quantity)> asks)> 
        GetOrderBookDepthAsync(string symbol, int depth = 20)
    {
        var bids = await AggregateOrderBookAsync(symbol, OrderSide.Buy, depth);
        var asks = await AggregateOrderBookAsync(symbol, OrderSide.Sell, depth);

        return (bids, asks);
    }

    private async Task<List<(decimal price, decimal quantity)>> AggregateOrderBookAsync(string symbol, OrderSide side, int depth)
    {
        var orders = await GetActiveOrdersAsync(symbol, side, depth * 2);

        // 按价格聚合数量
        var aggregated = orders
            .Where(o => o.Price.HasValue)
            .GroupBy(o => o.Price!.Value)
            .Select(g => (price: g.Key, quantity: g.Sum(o => o.Quantity - o.FilledQuantity)))
            .OrderBy(x => side == OrderSide.Buy ? -x.price : x.price)
            .Take(depth)
            .ToList();

        return aggregated;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 将 Redis Hash 映射为 Order 对象
    /// </summary>
    private DomainOrder MapToOrder(Dictionary<string, string> data)
    {
        if (data == null || data.Count == 0) return null!;

        return new DomainOrder
        {
            Id = int.Parse(data["id"]),
            UserId = string.IsNullOrEmpty(data["userId"]) ? null : int.Parse(data["userId"]),
            TradingPairId = int.Parse(data["tradingPairId"]),
            Side = (OrderSide)int.Parse(data["side"]),
            Type = (OrderType)int.Parse(data["type"]),
            Price = decimal.TryParse(data["price"], out var price) && price > 0 ? price : (decimal?)null,
            Quantity = decimal.Parse(data["quantity"]),
            FilledQuantity = decimal.Parse(data["filledQuantity"]),
            Status = (OrderStatus)int.Parse(data["status"]),
            CreatedAt = long.Parse(data["createdAt"]),
            UpdatedAt = long.Parse(data["updatedAt"])
        };
    }

    /// <summary>
    /// 加入 MySQL 同步队列
    /// </summary>
    private async Task EnqueueSyncOperation(string queueName, object data)
    {
        var json = JsonSerializer.Serialize(data);
        try
        {
            await _redis.ListLeftPushAsync($"sync_queue:{queueName}", json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue sync operation");
        }
    }

    #endregion
}
