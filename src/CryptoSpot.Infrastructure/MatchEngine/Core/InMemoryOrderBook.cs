using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Infrastructure.MatchEngine.Core
{
    public class InMemoryOrderBook : IOrderBook
    {
        public string Symbol { get; }
        private readonly SortedDictionary<decimal, OrderLevel> _bids = new(new DescComparer());
        private readonly SortedDictionary<decimal, OrderLevel> _asks = new();
        private readonly object _syncRoot = new();

        public InMemoryOrderBook(string symbol)
        {
            Symbol = symbol;
        }

        object IOrderBook.SyncRoot => _syncRoot;

        public void Add(Order order)
        {
            lock (_syncRoot)
            {
                var dict = order.Side == OrderSide.Buy ? _bids : _asks;
                var price = order.Price ?? 0m;
                if (!dict.TryGetValue(price, out var level))
                {
                    level = new OrderLevel(price);
                    dict[price] = level;
                }
                level.Add(order);
            }
        }

        public Order? GetBestOpposite(OrderSide side)
        {
            lock (_syncRoot)
            {
                var dict = side == OrderSide.Buy ? _asks : _bids;
                if (dict.Count == 0) return null;
                var level = dict.Values.First();
                var order = level.Best;
                if (order != null) return order;
                dict.Remove(level.Price);
                return GetBestOpposite(side);
            }
        }

        public void Remove(Order order)
        {
            lock (_syncRoot)
            {
                var dict = order.Side == OrderSide.Buy ? _bids : _asks;
                var price = order.Price ?? 0m;
                if (dict.TryGetValue(price, out var level))
                {
                    level.Remove(order);
                    if (level.Count == 0)
                        dict.Remove(price);
                }
            }
        }

        public bool RemoveById(long orderId)
        {
            lock (_syncRoot)
            {
                return RemoveById(_bids, orderId) || RemoveById(_asks, orderId);
            }
        }

        public IReadOnlyList<(decimal price, decimal quantity)> GetDepth(OrderSide side, int depth)
        {
            var dict = side == OrderSide.Buy ? _bids : _asks;
            var result = new List<(decimal price, decimal quantity)>(depth);
            lock (_syncRoot)
            {
                foreach (var kvp in dict)
                {
                    if (result.Count >= depth) break;
                    var qty = kvp.Value.TotalQuantity;
                    if (qty > 0)
                        result.Add((kvp.Key, qty));
                }
            }
            return result;
        }

        private static bool RemoveById(SortedDictionary<decimal, OrderLevel> dict, long orderId)
        {
            foreach (var (price, level) in dict.ToList())
            {
                if (!level.RemoveById(orderId))
                {
                    continue;
                }

                if (level.Count == 0)
                {
                    dict.Remove(price);
                }

                return true;
            }

            return false;
        }

        private class OrderLevel(decimal price)
        {
            public decimal Price { get; } = price;
            public int Count => _orders.Count;
            public decimal TotalQuantity => _orders.Sum(o => o.Quantity - o.FilledQuantity);

            private readonly List<Order> _orders = new();
            private int _bestIndex;

            public Order? Best
            {
                get
                {
                    while (_bestIndex < _orders.Count)
                    {
                        var o = _orders[_bestIndex];
                        if (o.FilledQuantity < o.Quantity) return o;
                        _bestIndex++;
                    }
                    return null;
                }
            }

            public void Add(Order order) => _orders.Add(order);

            public void Remove(Order order)
            {
                for (int i = 0; i < _orders.Count; i++)
                {
                    if (_orders[i].Id == order.Id)
                    {
                        _orders.RemoveAt(i);
                        if (i < _bestIndex) _bestIndex--;
                        return;
                    }
                }
            }

            public bool RemoveById(long orderId)
            {
                for (int i = 0; i < _orders.Count; i++)
                {
                    if (_orders[i].Id == orderId)
                    {
                        _orders.RemoveAt(i);
                        if (i < _bestIndex) _bestIndex--;
                        return true;
                    }
                }

                return false;
            }
        }

        private class DescComparer : IComparer<decimal>
        {
            public int Compare(decimal x, decimal y) => y.CompareTo(x);
        }
    }
}
