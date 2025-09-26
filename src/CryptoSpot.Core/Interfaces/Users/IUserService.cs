using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Core.Interfaces.Users
{
    /// <summary>
    /// 用户服务接口
    /// </summary>
    public interface IUserService
    {
        /// <summary>
        /// 根据ID获取用户
        /// </summary>
        Task<User?> GetUserByIdAsync(int userId);
        
        /// <summary>
        /// 根据用户名获取用户
        /// </summary>
        Task<User?> GetUserByUsernameAsync(string username);
        
        /// <summary>
        /// 根据邮箱获取用户
        /// </summary>
        Task<User?> GetUserByEmailAsync(string email);
        
        /// <summary>
        /// 获取所有系统用户
        /// </summary>
        Task<IEnumerable<User>> GetSystemUsersAsync();
        
        /// <summary>
        /// 获取活跃的系统用户
        /// </summary>
        Task<IEnumerable<User>> GetActiveSystemUsersAsync();
        
        /// <summary>
        /// 创建用户
        /// </summary>
        Task<User> CreateUserAsync(User user);
        
        /// <summary>
        /// 更新用户
        /// </summary>
        Task UpdateUserAsync(User user);
        
        /// <summary>
        /// 删除用户
        /// </summary>
        Task DeleteUserAsync(int userId);
    }
}
