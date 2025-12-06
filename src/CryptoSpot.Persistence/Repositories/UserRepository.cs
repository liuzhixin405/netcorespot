using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Persistence.Repositories;

public class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory) : base(dbContextFactory) { }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<User>().FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<User>().FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> ValidateCredentialsAsync(string username, string password)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<User>().FirstOrDefaultAsync(u => (u.Username == username || u.Email == username) && u.PasswordHash == password);
    }

    public async Task<bool> UsernameExistsAsync(string username)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<User>().AnyAsync(u => u.Username == username);
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<User>().AnyAsync(u => u.Email == email);
    }

    public async Task<bool> UpdateLastLoginAsync(long userId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var user = await context.Set<User>().FindAsync(userId);
        if (user == null) return false;
        user.LastLoginAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        user.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        context.Set<User>().Update(user);
        await context.SaveChangesAsync();
        return true;
    }

    public Task<UserStatistics> GetUserStatisticsAsync(long userId) => Task.FromResult(new UserStatistics());
}
