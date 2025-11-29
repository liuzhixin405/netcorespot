namespace CryptoSpot.Domain.Matching;

/// <summary>
/// 订单簿深度（买盘或卖盘）
/// 使用 SortedDictionary 维护价格-时间优先级
/// </summary>
public sealed class Depth
{
    private readonly Dictionary<long, BookOrder> _orders = new();
    private readonly SortedDictionary<(decimal Price, long OrderId), long> _queue;

    private Depth(IComparer<(decimal Price, long OrderId)> comparer)
    {
        _queue = new SortedDictionary<(decimal Price, long OrderId), long>(comparer);
    }

    /// <summary>
    /// 创建卖盘（价格升序）
    /// </summary>
    public static Depth CreateAskDepth() =>
        new(Comparer<(decimal Price, long OrderId)>.Create((a, b) =>
        {
            var priceCmp = a.Price.CompareTo(b.Price);
            if (priceCmp != 0) return priceCmp;
            return a.OrderId.CompareTo(b.OrderId);
        }));

    /// <summary>
    /// 创建买盘（价格降序）
    /// </summary>
    public static Depth CreateBidDepth() =>
        new(Comparer<(decimal Price, long OrderId)>.Create((a, b) =>
        {
            var priceCmp = b.Price.CompareTo(a.Price);
            if (priceCmp != 0) return priceCmp;
            return a.OrderId.CompareTo(b.OrderId);
        }));

    /// <summary>
    /// 按照撮合优先级迭代订单（价格-时间优先）
    /// </summary>
    public IEnumerable<BookOrder> IterateInMatchOrder()
    {
        foreach (var kv in _queue)
        {
            if (_orders.TryGetValue(kv.Value, out var order))
            {
                yield return order;
            }
        }
    }

    public void Add(BookOrder order)
    {
        _orders[order.OrderId] = order;
        _queue[(order.Price, order.OrderId)] = order.OrderId;
    }

    public void DecreaseSize(long orderId, decimal size)
    {
        if (!_orders.TryGetValue(orderId, out var order))
            throw new InvalidOperationException($"Order {orderId} not found");

        if (order.Size < size)
            throw new InvalidOperationException($"Order {orderId} size {order.Size} less than {size}");

        order.Size -= size;
        if (order.Size == 0)
        {
            _orders.Remove(orderId);
            _queue.Remove((order.Price, order.OrderId));
        }
    }

    public bool TryGet(long orderId, out BookOrder? order)
    {
        if (_orders.TryGetValue(orderId, out var o))
        {
            order = o;
            return true;
        }

        order = null;
        return false;
    }

    public IEnumerable<BookOrder> AllOrders() => _orders.Values;
    
    public int Count => _orders.Count;
}
