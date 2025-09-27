using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Services.Users
{
    /// <summary>
    /// 领域用户服务接口（返回领域实体），供 DTO 层 UserServiceV2 与 AuthService 使用。
    /// </summary>
    public interface IUserService
    {
        Task<User?> GetUserByIdAsync(int userId);
        Task<User?> GetUserByUsernameAsync(string username);
        Task<User?> GetUserByEmailAsync(string email);
        Task<IEnumerable<User>> GetSystemUsersAsync();
        Task<IEnumerable<User>> GetActiveSystemUsersAsync();
        Task<User> CreateUserAsync(User user);
        Task UpdateUserAsync(User user);
        Task DeleteUserAsync(int userId);
    }
}
