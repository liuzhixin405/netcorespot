using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Users
{
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
