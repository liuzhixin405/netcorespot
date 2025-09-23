using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Repositories
{
    public class AssetRepository : BaseRepository<Asset>, IAssetRepository
    {
        public AssetRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<Asset?> GetUserAssetAsync(int userId, string symbol)
        {
            return await _dbSet
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Symbol == symbol);
        }

        public async Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId)
        {
            return await _dbSet
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.Symbol)
                .ToListAsync();
        }

        public async Task<IEnumerable<Asset>> GetAssetsByUserIdAsync(int userId)
        {
            return await GetUserAssetsAsync(userId);
        }

        public async Task<Asset?> GetAssetByUserIdAndSymbolAsync(int userId, string symbol)
        {
            return await GetUserAssetAsync(userId, symbol);
        }

        public async Task<bool> UpdateBalanceAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetUserAssetAsync(userId, symbol);
            if (asset == null)
            {
                // 创建新资产记录
                asset = new Asset
                {
                    UserId = userId,
                    Symbol = symbol,
                    Available = amount,
                    Frozen = 0,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                await _dbSet.AddAsync(asset);
            }
            else
            {
                asset.Available += amount;
                asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _dbSet.Update(asset);
            }
            return true;
        }

        public async Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetUserAssetAsync(userId, symbol);
            if (asset == null || asset.Available < amount)
                return false;

            asset.Available -= amount;
            asset.Frozen += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _dbSet.Update(asset);
            return true;
        }

        public async Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetUserAssetAsync(userId, symbol);
            if (asset == null || asset.Frozen < amount)
                return false;

            asset.Frozen -= amount;
            asset.Available += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _dbSet.Update(asset);
            return true;
        }

        public async Task<AssetStatistics> GetAssetStatisticsAsync(int userId)
        {
            var assets = await GetUserAssetsAsync(userId);
            var assetList = assets.ToList();

            return new AssetStatistics
            {
                TotalAssets = assetList.Count,
                TotalBalance = assetList.Sum(a => a.Available + a.Frozen),
                TotalFrozen = assetList.Sum(a => a.Frozen),
                TotalAvailable = assetList.Sum(a => a.Available),
                AssetBalances = assetList.ToDictionary(a => a.Symbol, a => a.Available + a.Frozen)
            };
        }
    }
}
