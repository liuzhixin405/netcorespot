using System.Collections.Concurrent;
using CryptoSpot.Core.Interfaces.Trading;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 内存实现的订单簿快照缓存。后续可替换为 Redis。
    /// </summary>
    public class OrderBookSnapshotCache : IOrderBookSnapshotCache
    {
        private readonly ConcurrentDictionary<string, (IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long ts)> _cache = new();
        public void Update(string symbol, IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp)
        {
            _cache[symbol] = (bids, asks, timestamp);
        }
        public (IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp)? Get(string symbol)
        {
            if (_cache.TryGetValue(symbol, out var v)) return v;
            return null;
        }
    }
}
