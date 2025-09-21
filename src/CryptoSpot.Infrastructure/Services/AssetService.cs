using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class AssetService : IAssetService
    {
        private readonly IRepository<Asset> _assetRepository;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<AssetService> _logger;

        public AssetService(
            IRepository<Asset> assetRepository,
            IUserRepository userRepository,
            ILogger<AssetService> logger)
        {
            _assetRepository = assetRepository;
            _userRepository = userRepository;
            _logger = logger;
        }

        public async Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId)
        {
            try
            {
                return await _assetRepository.FindAsync(a => a.UserId == userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assets for user {UserId}", userId);
                return new List<Asset>();
            }
        }

        public async Task<Asset?> GetUserAssetAsync(int userId, string symbol)
        {
            try
            {
                return await _assetRepository.FirstOrDefaultAsync(a => a.UserId == userId && a.Symbol == symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting asset {Symbol} for user {UserId}", symbol, userId);
                return null;
            }
        }

        public async Task<Asset> CreateUserAssetAsync(int userId, string symbol, decimal available = 0, decimal frozen = 0)
        {
            try
            {
                var existingAsset = await GetUserAssetAsync(userId, symbol);
                
                if (existingAsset != null)
                {
                    existingAsset.Available = available;
                    existingAsset.Frozen = frozen;
                    existingAsset.UpdatedAt = DateTime.UtcNow;
                    await _assetRepository.UpdateAsync(existingAsset);
                    return existingAsset;
                }
                else
                {
                    var newAsset = new Asset
                    {
                        UserId = userId,
                        Symbol = symbol,
                        Available = available,
                        Frozen = frozen,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    
                    return await _assetRepository.AddAsync(newAsset);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating/updating asset {Symbol} for user {UserId}", symbol, userId);
                throw;
            }
        }

        public async Task<Asset> UpdateAssetBalanceAsync(int userId, string symbol, decimal available, decimal frozen)
        {
            try
            {
                var asset = await GetUserAssetAsync(userId, symbol);
                if (asset == null)
                {
                    return await CreateUserAssetAsync(userId, symbol, available, frozen);
                }

                asset.Available = available;
                asset.Frozen = frozen;
                asset.UpdatedAt = DateTime.UtcNow;
                await _assetRepository.UpdateAsync(asset);
                
                return asset;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating asset balance {Symbol} for user {UserId}", symbol, userId);
                throw;
            }
        }

        public async Task<bool> HasSufficientBalanceAsync(int userId, string symbol, decimal amount, bool includeFrozen = false)
        {
            try
            {
                var asset = await GetUserAssetAsync(userId, symbol);
                if (asset == null) return false;
                
                var totalBalance = includeFrozen ? asset.Available + asset.Frozen : asset.Available;
                return totalBalance >= amount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking balance for user {UserId}, symbol {Symbol}", userId, symbol);
                return false;
            }
        }

        public async Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount)
        {
            try
            {
                var asset = await GetUserAssetAsync(userId, symbol);
                if (asset == null || asset.Available < amount)
                {
                    _logger.LogWarning("Insufficient balance to freeze {Amount} {Symbol} for user {UserId}", amount, symbol, userId);
                    return false;
                }

                asset.Available -= amount;
                asset.Frozen += amount;
                asset.UpdatedAt = DateTime.UtcNow;
                
                await _assetRepository.UpdateAsync(asset);
                
                _logger.LogInformation("Froze {Amount} {Symbol} for user {UserId}", amount, symbol, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error freezing asset {Symbol} for user {UserId}", symbol, userId);
                return false;
            }
        }

        public async Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount)
        {
            try
            {
                var asset = await GetUserAssetAsync(userId, symbol);
                if (asset == null || asset.Frozen < amount)
                {
                    _logger.LogWarning("Insufficient frozen balance to unfreeze {Amount} {Symbol} for user {UserId}", amount, symbol, userId);
                    return false;
                }

                asset.Frozen -= amount;
                asset.Available += amount;
                asset.UpdatedAt = DateTime.UtcNow;
                
                await _assetRepository.UpdateAsync(asset);
                
                _logger.LogInformation("Unfroze {Amount} {Symbol} for user {UserId}", amount, symbol, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unfreezing asset {Symbol} for user {UserId}", symbol, userId);
                return false;
            }
        }

        public async Task<bool> DeductAssetAsync(int userId, string symbol, decimal amount, bool fromFrozen = false)
        {
            try
            {
                var asset = await GetUserAssetAsync(userId, symbol);
                if (asset == null)
                {
                    _logger.LogWarning("Asset {Symbol} not found for user {UserId}", symbol, userId);
                    return false;
                }

                if (fromFrozen)
                {
                    if (asset.Frozen < amount)
                    {
                        _logger.LogWarning("Insufficient frozen balance to deduct {Amount} {Symbol} for user {UserId}", amount, symbol, userId);
                        return false;
                    }
                    asset.Frozen -= amount;
                }
                else
                {
                    if (asset.Available < amount)
                    {
                        _logger.LogWarning("Insufficient available balance to deduct {Amount} {Symbol} for user {UserId}", amount, symbol, userId);
                        return false;
                    }
                    asset.Available -= amount;
                }

                asset.UpdatedAt = DateTime.UtcNow;
                await _assetRepository.UpdateAsync(asset);
                
                _logger.LogInformation("Deducted {Amount} {Symbol} from user {UserId} ({Source})", 
                    amount, symbol, userId, fromFrozen ? "frozen" : "available");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deducting asset {Symbol} for user {UserId}", symbol, userId);
                return false;
            }
        }

        public async Task<bool> AddAssetAsync(int userId, string symbol, decimal amount)
        {
            try
            {
                var asset = await GetUserAssetAsync(userId, symbol);
                if (asset == null)
                {
                    // 创建新资产
                    await CreateUserAssetAsync(userId, symbol, amount, 0);
                }
                else
                {
                    asset.Available += amount;
                    asset.UpdatedAt = DateTime.UtcNow;
                    await _assetRepository.UpdateAsync(asset);
                }
                
                _logger.LogInformation("Added {Amount} {Symbol} to user {UserId}", amount, symbol, userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding asset {Symbol} for user {UserId}", symbol, userId);
                return false;
            }
        }

        public async Task InitializeUserAssetsAsync(int userId, Dictionary<string, decimal> initialBalances)
        {
            try
            {
                foreach (var balance in initialBalances)
                {
                    await CreateUserAssetAsync(userId, balance.Key, balance.Value, 0);
                }
                
                _logger.LogInformation("Initialized assets for user {UserId} with {Count} assets", userId, initialBalances.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing assets for user {UserId}", userId);
                throw;
            }
        }
    }
}
