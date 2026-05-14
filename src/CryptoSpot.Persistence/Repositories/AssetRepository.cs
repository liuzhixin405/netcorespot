using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Persistence.Repositories;

public class AssetRepository : BaseRepository<Asset>, IAssetRepository
{
    public AssetRepository(ApplicationDbContext dbContext) : base(dbContext) { }

    public async Task<Asset?> GetUserAssetAsync(long userId, string symbol)
    {
        return await _dbContext.Set<Asset>().AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Symbol == symbol);
    }

    public async Task<IEnumerable<Asset>> GetUserAssetsAsync(long userId)
    {
        return await _dbContext.Set<Asset>().AsNoTracking()
            .Where(a => a.UserId == userId).OrderBy(a => a.Symbol).ToListAsync();
    }

    public async Task<IEnumerable<Asset>> GetAssetsByUserIdAsync(long userId)
        => await GetUserAssetsAsync(userId);

    public async Task<Asset?> GetAssetByUserIdAndSymbolAsync(long userId, string symbol)
        => await GetUserAssetAsync(userId, symbol);

    public async Task<bool> UpdateBalanceAsync(long userId, string symbol, decimal amount)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rows = await _dbContext.Set<Asset>()
            .Where(a => a.UserId == userId && a.Symbol == symbol)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Available, a => a.Available + amount)
                .SetProperty(a => a.UpdatedAt, now));

        if (rows > 0) return true;

        var exists = await _dbContext.Set<Asset>().AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.Symbol == symbol);
        if (exists) return true; // concurrent update already handled it

        var asset = new Asset
        {
            UserId = userId,
            Symbol = symbol,
            Available = amount,
            Frozen = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _dbContext.Set<Asset>().AddAsync(asset);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> FreezeAssetAsync(long userId, string symbol, decimal amount)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rows = await _dbContext.Set<Asset>()
            .Where(a => a.UserId == userId && a.Symbol == symbol && a.Available >= amount)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Available, a => a.Available - amount)
                .SetProperty(a => a.Frozen, a => a.Frozen + amount)
                .SetProperty(a => a.UpdatedAt, now));
        return rows > 0;
    }

    public async Task<bool> UnfreezeAssetAsync(long userId, string symbol, decimal amount)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rows = await _dbContext.Set<Asset>()
            .Where(a => a.UserId == userId && a.Symbol == symbol && a.Frozen >= amount)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Frozen, a => a.Frozen - amount)
                .SetProperty(a => a.Available, a => a.Available + amount)
                .SetProperty(a => a.UpdatedAt, now));
        return rows > 0;
    }
}
