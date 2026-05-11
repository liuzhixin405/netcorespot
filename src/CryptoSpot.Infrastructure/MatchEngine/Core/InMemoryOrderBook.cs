using CryptoSpot.Domain.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CryptoSpot.Infrastructure.MatchEngine.Core
{
    public class InMemoryOrderBook : IOrderBook
    {
        public string Symbol { get; }
        private readonly SortedDictionary<decimal, Queue<Order>> _bids = new(new DescComparer());
        private readonly SortedDictionary<decimal, Queue<Order>> _asks = new();
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
                if (!dict.TryGetValue(price, out var q))
                {
                    q = new Queue<Order>();
                    dict[price] = q;
                }
                q.Enqueue(order);
            }
        }

        public Order? GetBestOpposite(OrderSide side)
        {
            lock (_syncRoot)
            {
                var dict = side == OrderSide.Buy ? _asks : _bids;
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
                dict.Remove(first.Key);
                return GetBestOpposite(side);
            }
        }

        public void Remove(Order order)
        {
            lock (_syncRoot)
            {
                var dict = order.Side == OrderSide.Buy ? _bids : _asks;
                var price = order.Price ?? 0m;
                if (dict.TryGetValue(price, out var q))
                {
                    var list = q.ToList();
                    list.RemoveAll(o => o.Id == order.Id);
                    if (list.Count == 0)
                        dict.Remove(price);
                    else
                        dict[price] = new Queue<Order>(list);
                }
            }
        }

        public IReadOnlyList<(decimal price, decimal quantity)> GetDepth(OrderSide side, int depth)
        {
            var dict = side == OrderSide.Buy ? _bids : _asks;
            var result = new List<(decimal price, decimal quantity)>(depth);
            decimal[] keys;
            lock (_syncRoot)
            {
                keys = dict.Keys.Take(depth).ToArray();
            }
            foreach (var key in keys)
            {
                lock (_syncRoot)
                {
                    if (!dict.TryGetValue(key, out var q)) continue;
                    var total = q.Where(o => o.FilledQuantity < o.Quantity)
                                 .Sum(o => (o.Quantity - o.FilledQuantity));
                    if (total > 0)
                        result.Add((key, total));
                }
            }
            return result;
        }

        private class DescComparer : IComparer<decimal>
        {
            public int Compare(decimal x, decimal y) => y.CompareTo(x);
        }
    }
}
