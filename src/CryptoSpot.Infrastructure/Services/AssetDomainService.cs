using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.Abstractions.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 领域资产服务实现：直接操作仓储，返回领域实体。简化实现，使系统恢复可编译；后续可扩展缓存/批量Flush。
    /// </summary>
    public class AssetDomainService : IAssetDomainService
    {
        private readonly IAssetRepository _assetRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<AssetDomainService> _logger;

        public AssetDomainService(
            IAssetRepository assetRepository,
            IUnitOfWork unitOfWork,
            ILogger<AssetDomainService> logger)
        {
            _assetRepository = assetRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId) => _assetRepository.FindAsync(a => a.UserId == userId);

        public async Task<Asset?> GetUserAssetAsync(int userId, string symbol)
        {
            var assets = await _assetRepository.FindAsync(a => a.UserId == userId && a.Symbol == symbol);
            return assets.FirstOrDefault();
        }

        public async Task<Asset> CreateUserAssetAsync(int userId, string symbol, decimal available = 0, decimal frozen = 0)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var asset = new Asset
            {
                UserId = userId,
                Symbol = symbol,
                Available = available,
                Frozen = frozen,
                CreatedAt = now,
                UpdatedAt = now
            };
            return await _assetRepository.AddAsync(asset);
        }

        public async Task<Asset> UpdateAssetBalanceAsync(int userId, string symbol, decimal available, decimal frozen)
        {
            var asset = await GetOrCreateAsync(userId, symbol);
            asset.Available = available;
            asset.Frozen = frozen;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return asset;
        }

        public async Task<bool> HasSufficientBalanceAsync(int userId, string symbol, decimal amount, bool includeFrozen = false)
        {
            var asset = await GetUserAssetAsync(userId, symbol);
            if (asset == null) return false;
            var balance = includeFrozen ? asset.Total : asset.Available;
            return balance >= amount;
        }

        public async Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetOrCreateAsync(userId, symbol);
            if (asset.Available < amount) return false;
            asset.Available -= amount;
            asset.Frozen += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return true;
        }

        public async Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetOrCreateAsync(userId, symbol);
            if (asset.Frozen < amount) return false;
            asset.Frozen -= amount;
            asset.Available += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return true;
        }

        public async Task<bool> DeductAssetAsync(int userId, string symbol, decimal amount, bool fromFrozen = false)
        {
            var asset = await GetOrCreateAsync(userId, symbol);
            if (fromFrozen)
            {
                if (asset.Frozen < amount) return false;
                asset.Frozen -= amount;
            }
            else
            {
                if (asset.Available < amount) return false;
                asset.Available -= amount;
            }
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return true;
        }

        public async Task<bool> AddAssetAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetOrCreateAsync(userId, symbol);
            asset.Available += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return true;
        }

        public async Task InitializeUserAssetsAsync(int userId, Dictionary<string, decimal> initialBalances)
        {
            foreach (var kv in initialBalances)
            {
                await AddAssetAsync(userId, kv.Key, kv.Value);
            }
        }

        private async Task<Asset> GetOrCreateAsync(int userId, string symbol)
        {
            var existing = await GetUserAssetAsync(userId, symbol);
            if (existing != null) return existing;
            return await CreateUserAssetAsync(userId, symbol);
        }
    }
}
