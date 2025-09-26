// filepath: g:\github\netcorespot\src\CryptoSpot.Core\Interfaces\Trading\IOrderBookSnapshotCache.cs
using System.Threading;
using System.Threading.Tasks;

namespace CryptoSpot.Core.Interfaces.Trading
{
    /// <summary>
    /// 缓存最新外部(流式)订单簿快照, 供新订阅客户端立即获取, 避免等待下一次增量推送导致的空白闪烁
    /// </summary>
    public interface IOrderBookSnapshotCache
    {
        void Update(string symbol, IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp);
        (IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp)? Get(string symbol);
        // 新增: 从远程(如 Redis) 预热/加载
        Task<bool> TryLoadAsync(string symbol, CancellationToken ct = default);
    }
}
