using CryptoSpot.Domain.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CryptoSpot.Infrastructure.MatchEngine.Core
{
    /// <summary>
    /// 与旧 InMemoryMatchEngineService 内部类逻辑等价的抽离版本，便于后续独立测试与替换。
    /// </summary>
    public class InMemoryOrderBook : IOrderBook
    {
        public string Symbol { get; }
        private readonly SortedDictionary<decimal, Queue<Order>> _bids = new(new DescComparer());
        private readonly SortedDictionary<decimal, Queue<Order>> _asks = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        public object SyncRoot => _lock; // 暴露锁对象以兼容现有 per-symbol 串行逻辑

        public InMemoryOrderBook(string symbol)
        {
            Symbol = symbol;
        }

        public void Add(Order order)
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

        public Order? GetBestOpposite(OrderSide side)
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

        public void Remove(Order order)
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

        public IReadOnlyList<(decimal price, decimal quantity)> GetDepth(OrderSide side, int depth)
        {
            var dict = side == OrderSide.Buy ? _bids : _asks;
            var result = new List<(decimal price, decimal quantity)>(depth);
            foreach (var kv in dict.Take(depth))
            {
                var total = kv.Value.Where(o => o.FilledQuantity < o.Quantity)
                                     .Sum(o => (o.Quantity - o.FilledQuantity));
                if (total > 0)
                    result.Add((kv.Key, total));
            }
            return result;
        }

        private class DescComparer : IComparer<decimal>
        {
            public int Compare(decimal x, decimal y) => y.CompareTo(x);
        }
    }
}
