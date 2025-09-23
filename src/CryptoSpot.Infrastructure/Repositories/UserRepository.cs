using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Repositories
{
    public class UserRepository : BaseRepository<User>, IUserRepository
    {
        public UserRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            return await _dbSet
                .FirstOrDefaultAsync(u => u.Username == username);
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _dbSet
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User?> ValidateCredentialsAsync(string username, string password)
        {
            var user = await _dbSet
                .FirstOrDefaultAsync(u => (u.Username == username || u.Email == username) && u.PasswordHash == password);
            return user;
        }

        public async Task<bool> UsernameExistsAsync(string username)
        {
            return await _dbSet
                .AnyAsync(u => u.Username == username);
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            return await _dbSet
                .AnyAsync(u => u.Email == email);
        }

        public async Task<bool> UpdateLastLoginAsync(int userId)
        {
            var user = await _dbSet.FindAsync(userId);
            if (user == null)
                return false;

            user.LastLoginAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            user.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _dbSet.Update(user);
            return true;
        }

        public async Task<UserStatistics> GetUserStatisticsAsync(int userId)
        {
            // 这里需要从其他表获取统计数据
            // 暂时返回默认值，实际实现中应该注入其他仓储或使用复杂查询
            return new UserStatistics
            {
                TotalOrders = 0,
                TotalTrades = 0,
                TotalVolume = 0,
                TotalValue = 0,
                LastLoginTime = null,
                LastOrderTime = null,
                LastTradeTime = null
            };
        }
    }
}