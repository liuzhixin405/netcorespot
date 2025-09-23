using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.Repositories
{
    /// <summary>
    /// 用户仓储接口
    /// </summary>
    public interface IUserRepository : IRepository<User>
    {
        /// <summary>
        /// 根据用户名获取用户
        /// </summary>
        /// <param name="username">用户名</param>
        /// <returns>用户对象</returns>
        Task<User?> GetByUsernameAsync(string username);

        /// <summary>
        /// 根据邮箱获取用户
        /// </summary>
        /// <param name="email">邮箱</param>
        /// <returns>用户对象</returns>
        Task<User?> GetByEmailAsync(string email);

        /// <summary>
        /// 验证用户凭据
        /// </summary>
        /// <param name="username">用户名或邮箱</param>
        /// <param name="password">密码</param>
        /// <returns>用户对象</returns>
        Task<User?> ValidateCredentialsAsync(string username, string password);

        /// <summary>
        /// 检查用户名是否存在
        /// </summary>
        /// <param name="username">用户名</param>
        /// <returns>是否存在</returns>
        Task<bool> UsernameExistsAsync(string username);

        /// <summary>
        /// 检查邮箱是否存在
        /// </summary>
        /// <param name="email">邮箱</param>
        /// <returns>是否存在</returns>
        Task<bool> EmailExistsAsync(string email);

        /// <summary>
        /// 更新用户最后登录时间
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdateLastLoginAsync(int userId);

        /// <summary>
        /// 获取用户统计信息
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>用户统计</returns>
        Task<UserStatistics> GetUserStatisticsAsync(int userId);
    }

    /// <summary>
    /// 用户统计信息
    /// </summary>
    public class UserStatistics
    {
        public int TotalOrders { get; set; }
        public int TotalTrades { get; set; }
        public decimal TotalVolume { get; set; }
        public decimal TotalValue { get; set; }
        public DateTime? LastLoginTime { get; set; }
        public DateTime? LastOrderTime { get; set; }
        public DateTime? LastTradeTime { get; set; }
    }
}