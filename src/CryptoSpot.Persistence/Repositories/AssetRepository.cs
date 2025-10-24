// filepath: g:\github\netcorespot\src\CryptoSpot.Persistence\Repositories\AssetRepository.cs
using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Persistence.Repositories;

public class AssetRepository : BaseRepository<Asset>, IAssetRepository
{
    public AssetRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory) : base(dbContextFactory) { }

    public async Task<Asset?> GetUserAssetAsync(int userId, string symbol)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<Asset>().FirstOrDefaultAsync(a => a.UserId == userId && a.Symbol == symbol);
    }

    public async Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<Asset>().Where(a => a.UserId == userId).OrderBy(a => a.Symbol).ToListAsync();
    }

    public async Task<IEnumerable<Asset>> GetAssetsByUserIdAsync(int userId)
        => await GetUserAssetsAsync(userId);

    public async Task<Asset?> GetAssetByUserIdAndSymbolAsync(int userId, string symbol)
        => await GetUserAssetAsync(userId, symbol);

    public async Task<bool> UpdateBalanceAsync(int userId, string symbol, decimal amount)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var asset = await context.Set<Asset>().FirstOrDefaultAsync(a => a.UserId == userId && a.Symbol == symbol);
        if (asset == null)
        {
            asset = new Asset
            {
                UserId = userId,
                Symbol = symbol,
                Available = amount,
                Frozen = 0,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await context.Set<Asset>().AddAsync(asset);
        }
        else
        {
            asset.Available += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            context.Set<Asset>().Update(asset);
        }
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var asset = await context.Set<Asset>().FirstOrDefaultAsync(a => a.UserId == userId && a.Symbol == symbol);
        if (asset == null || asset.Available < amount) return false;
        asset.Available -= amount; asset.Frozen += amount; asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        context.Set<Asset>().Update(asset);
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var asset = await context.Set<Asset>().FirstOrDefaultAsync(a => a.UserId == userId && a.Symbol == symbol);
        if (asset == null || asset.Frozen < amount) return false;
        asset.Frozen -= amount; asset.Available += amount; asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        context.Set<Asset>().Update(asset);
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<AssetStatistics> GetAssetStatisticsAsync(int userId)
    {
        var assets = await GetUserAssetsAsync(userId);
        var list = assets.ToList();
        return new AssetStatistics
        {
            TotalAssets = list.Count,
            TotalBalance = list.Sum(a => a.Available + a.Frozen),
            TotalFrozen = list.Sum(a => a.Frozen),
            TotalAvailable = list.Sum(a => a.Available),
            AssetBalances = list.ToDictionary(a => a.Symbol, a => a.Available + a.Frozen)
        };
    }

    // 原子操作 - 直接使用 SQL 更新,避免 EF Core 并发冲突
    public async Task<int> AtomicDeductFrozenAsync(int userId, string symbol, decimal amount)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return await context.Database.ExecuteSqlRawAsync(
            @"UPDATE Assets 
              SET Frozen = Frozen - {0}, UpdatedAt = {1} 
              WHERE UserId = {2} AND Symbol = {3} AND Frozen >= {0}",
            amount, now, userId, symbol);
    }

    public async Task<int> AtomicAddAvailableAsync(int userId, string symbol, decimal amount)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return await context.Database.ExecuteSqlRawAsync(
            @"UPDATE Assets 
              SET Available = Available + {0}, UpdatedAt = {1} 
              WHERE UserId = {2} AND Symbol = {3}",
            amount, now, userId, symbol);
    }

    public async Task<int> AtomicDeductAvailableAsync(int userId, string symbol, decimal amount)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return await context.Database.ExecuteSqlRawAsync(
            @"UPDATE Assets 
              SET Available = Available - {0}, UpdatedAt = {1} 
              WHERE UserId = {2} AND Symbol = {3} AND Available >= {0}",
            amount, now, userId, symbol);
    }
}
