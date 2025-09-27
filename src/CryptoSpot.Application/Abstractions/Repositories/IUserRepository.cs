using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Repositories
{
    public interface IUserRepository : IRepository<User>
    {
        Task<User?> GetByUsernameAsync(string username);
        Task<User?> GetByEmailAsync(string email);
        Task<User?> ValidateCredentialsAsync(string username, string password);
        Task<bool> UsernameExistsAsync(string username);
        Task<bool> EmailExistsAsync(string email);
        Task<bool> UpdateLastLoginAsync(int userId);
        Task<UserStatistics> GetUserStatisticsAsync(int userId);
    }

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
