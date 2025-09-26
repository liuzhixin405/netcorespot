using System.Collections.Concurrent;
using CryptoSpot.Core.Interfaces.Trading;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using CryptoSpot.Redis;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 内存 + Redis 实现的订单簿快照缓存。
    /// </summary>
    public class OrderBookSnapshotCache : IOrderBookSnapshotCache
    {
        private readonly ConcurrentDictionary<string, (IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long ts)> _cache = new();
        private readonly IRedisCache? _redis; // optional
        private const string RedisKeyPrefix = "md:ob:"; // cryptospot: 由外层配置的 KeyPrefix 统一

        public OrderBookSnapshotCache(IRedisCache? redis = null)
        {
            _redis = redis;
        }
        public void Update(string symbol, IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp)
        {
            _cache[symbol] = (bids, asks, timestamp);
            if (_redis != null)
            {
                var dto = new PersistModel
                {
                    Ts = timestamp,
                    Bids = bids.Select(b => new PriceLevel { P = b.Price, Q = b.Quantity }).ToList(),
                    Asks = asks.Select(a => new PriceLevel { P = a.Price, Q = a.Quantity }).ToList()
                };
                try { _ = _redis.AddAsync(GetRedisKey(symbol), dto); } catch { }
            }
        }
        public (IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp)? Get(string symbol)
        {
            if (_cache.TryGetValue(symbol, out var v)) return v;
            return null;
        }
        public async Task<bool> TryLoadAsync(string symbol, CancellationToken ct = default)
        {
            if (_redis == null) return false;
            try
            {
                var dto = await _redis.GetAsync<PersistModel>(GetRedisKey(symbol));
                if (dto == null || dto.Bids == null || dto.Asks == null) return false;
                var bids = dto.Bids.Select(l => new OrderBookLevel { Price = l.P, Quantity = l.Q, Total = l.Q }).ToList();
                var asks = dto.Asks.Select(l => new OrderBookLevel { Price = l.P, Quantity = l.Q, Total = l.Q }).ToList();
                _cache[symbol] = (bids, asks, dto.Ts);
                return true;
            }
            catch { return false; }
        }
        private string GetRedisKey(string symbol) => RedisKeyPrefix + symbol.ToUpper();
        
        [MessagePack.MessagePackObject(true)]
        public class PersistModel
        {
            public long Ts { get; set; }
            public List<PriceLevel> Bids { get; set; } = new();
            public List<PriceLevel> Asks { get; set; } = new();
        }
        [MessagePack.MessagePackObject(true)]
        public class PriceLevel
        {
            public decimal P { get; set; }
            public decimal Q { get; set; }
        }
    }
}
