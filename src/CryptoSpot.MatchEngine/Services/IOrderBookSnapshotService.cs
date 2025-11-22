using System.Threading;
using System.Threading.Tasks;

namespace CryptoSpot.MatchEngine.Services
{
    /// <summary>
    /// 订单簿快照推送服务接口
    /// </summary>
    public interface IOrderBookSnapshotService
    {
        /// <summary>
        /// 推送订单簿快照给实时推送服务
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <param name="depth">深度（默认20档）</param>
        /// <param name="ct">取消令牌</param>
        Task PushSnapshotAsync(string symbol, int depth = 20, CancellationToken ct = default);
    }
}
