using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.System
{
    /// <summary>
    /// 系统账号服务接口
    /// </summary>
    public interface ISystemAccountService
    {
        /// <summary>
        /// 获取系统账号
        /// </summary>
        Task<User?> GetSystemAccountAsync(int id);
        
        /// <summary>
        /// 获取指定类型的系统账号
        /// </summary>
        Task<IEnumerable<User>> GetSystemAccountsByTypeAsync(UserType type);
        
        /// <summary>
        /// 获取活跃的系统账号
        /// </summary>
        Task<IEnumerable<User>> GetActiveSystemAccountsAsync();
        
        /// <summary>
        /// 创建系统账号
        /// </summary>
        Task<User> CreateSystemAccountAsync(string name, UserType type, string description = "");
        
        /// <summary>
        /// 更新系统账号
        /// </summary>
        Task<User> UpdateSystemAccountAsync(User account);
        
        /// <summary>
        /// 启用/禁用自动交易
        /// </summary>
        Task SetAutoTradingStatusAsync(int accountId, bool enabled);
        
        /// <summary>
        /// 重置每日交易统计
        /// </summary>
        Task ResetDailyStatsAsync(int accountId);
    }
}
