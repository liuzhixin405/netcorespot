using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Persistence.Repositories;

public class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(ApplicationDbContext context) : base(context) { }

    public async Task<User?> GetByUsernameAsync(string username) => await _dbSet.FirstOrDefaultAsync(u => u.Username == username);
    public async Task<User?> GetByEmailAsync(string email) => await _dbSet.FirstOrDefaultAsync(u => u.Email == email);
    public async Task<User?> ValidateCredentialsAsync(string username, string password) => await _dbSet.FirstOrDefaultAsync(u => (u.Username == username || u.Email == username) && u.PasswordHash == password);
    public async Task<bool> UsernameExistsAsync(string username) => await _dbSet.AnyAsync(u => u.Username == username);
    public async Task<bool> EmailExistsAsync(string email) => await _dbSet.AnyAsync(u => u.Email == email);
    public async Task<bool> UpdateLastLoginAsync(int userId)
    {
        var user = await _dbSet.FindAsync(userId);
        if (user == null) return false;
        user.LastLoginAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        user.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _dbSet.Update(user);
        return true;
    }
    public Task<UserStatistics> GetUserStatisticsAsync(int userId) => Task.FromResult(new UserStatistics());
}
