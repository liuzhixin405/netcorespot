using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Persistence.Repositories;

public class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(ApplicationDbContext dbContext) : base(dbContext) { }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await _dbContext.Set<User>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _dbContext.Set<User>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> ValidateCredentialsAsync(string username, string password)
    {
        return await _dbContext.Set<User>().AsNoTracking()
            .FirstOrDefaultAsync(u => (u.Username == username || u.Email == username) && u.PasswordHash == password);
    }

    public async Task<bool> UsernameExistsAsync(string username)
    {
        return await _dbContext.Set<User>().AnyAsync(u => u.Username == username);
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _dbContext.Set<User>().AnyAsync(u => u.Email == email);
    }

    public async Task<bool> UpdateLastLoginAsync(long userId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rows = await _dbContext.Set<User>()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LastLoginAt, now)
                .SetProperty(u => u.UpdatedAt, now));
        return rows > 0;
    }

    public Task<UserStatistics> GetUserStatisticsAsync(long userId) => Task.FromResult(new UserStatistics());
}
