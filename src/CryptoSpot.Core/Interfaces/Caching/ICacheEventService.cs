namespace CryptoSpot.Core.Interfaces.Caching
{
    /// <summary>
    /// 缓存事件服务接口，用于通知缓存更新
    /// </summary>
    public interface ICacheEventService
    {
        /// <summary>
        /// 通知用户数据已变更
        /// </summary>
        Task NotifyUserChangedAsync(int userId);
        
        /// <summary>
        /// 通知交易对数据已变更
        /// </summary>
        Task NotifyTradingPairChangedAsync(string symbol);
        
        /// <summary>
        /// 订阅用户变更事件
        /// </summary>
        event Func<int, Task>? UserChanged;
        
        /// <summary>
        /// 订阅交易对变更事件
        /// </summary>
        event Func<string, Task>? TradingPairChanged;
    }
}
