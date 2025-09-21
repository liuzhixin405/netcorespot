using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.System;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class SystemAssetService : ISystemAssetService
    {
        private readonly IRepository<Asset> _assetRepository;
        private readonly ILogger<SystemAssetService> _logger;

        public SystemAssetService(
            IRepository<Asset> assetRepository,
            ILogger<SystemAssetService> logger)
        {
            _assetRepository = assetRepository;
            _logger = logger;
        }

        public async Task<IEnumerable<Asset>> GetSystemAssetsAsync(int systemAccountId)
        {
            try
            {
                return await _assetRepository.FindAsync(a => a.UserId == systemAccountId && a.UserId != null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system assets for account {AccountId}", systemAccountId);
                return new List<Asset>();
            }
        }

        public async Task<Asset?> GetSystemAssetAsync(int systemAccountId, string symbol)
        {
            try
            {
                var assets = await _assetRepository.FindAsync(a => a.UserId == systemAccountId && a.Symbol == symbol);
                return assets.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system asset {Symbol} for account {AccountId}", symbol, systemAccountId);
                return null;
            }
        }

        public async Task<Asset> CreateSystemAssetAsync(int systemAccountId, string symbol, decimal initialBalance = 0)
        {
            try
            {
                var systemAsset = new Asset
                {
                    UserId = systemAccountId,
                    Symbol = symbol,
                    Available = initialBalance,
                    Frozen = 0,
                    MinReserve = 0,
                    TargetBalance = initialBalance,
                };

                var createdAsset = await _assetRepository.AddAsync(systemAsset);
                _logger.LogInformation("Created system asset {Symbol} for account {SystemAccountId} with balance {Balance}", 
                    symbol, systemAccountId, initialBalance);

                return createdAsset;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating system asset {Symbol} for account {AccountId}", symbol, systemAccountId);
                throw;
            }
        }

        public async Task<Asset> UpdateAssetBalanceAsync(int systemAccountId, string symbol, decimal available, decimal frozen)
        {
            try
            {
                var asset = await GetSystemAssetAsync(systemAccountId, symbol);
                if (asset == null)
                {
                    throw new InvalidOperationException($"System asset {symbol} not found for account {systemAccountId}");
                }

                asset.Available = available;
                asset.Frozen = frozen;
                asset.Touch();
                await _assetRepository.UpdateAsync(asset);

                _logger.LogDebug("Updated system asset {Symbol} balance for account {AccountId}: Available={Available}, Frozen={Frozen}", 
                    symbol, systemAccountId, available, frozen);

                return asset;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating system asset {Symbol} balance for account {AccountId}", symbol, systemAccountId);
                throw;
            }
        }

        public async Task<bool> FreezeAssetAsync(int systemAccountId, string symbol, decimal amount)
        {
            try
            {
                var asset = await GetSystemAssetAsync(systemAccountId, symbol);
                if (asset == null || asset.Available < amount)
                {
                    return false;
                }

                asset.Available -= amount;
                asset.Frozen += amount;
                asset.Touch();
                
                await _assetRepository.UpdateAsync(asset);
                
                _logger.LogDebug("Frozen {Amount} {Symbol} for system account {AccountId}", amount, symbol, systemAccountId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error freezing asset {Symbol} for system account {AccountId}", symbol, systemAccountId);
                return false;
            }
        }

        public async Task<bool> UnfreezeAssetAsync(int systemAccountId, string symbol, decimal amount)
        {
            try
            {
                var asset = await GetSystemAssetAsync(systemAccountId, symbol);
                if (asset == null || asset.Frozen < amount)
                {
                    return false;
                }

                asset.Frozen -= amount;
                asset.Available += amount;
                asset.Touch();
                
                await _assetRepository.UpdateAsync(asset);
                
                _logger.LogDebug("Unfrozen {Amount} {Symbol} for system account {AccountId}", amount, symbol, systemAccountId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unfreezing asset {Symbol} for system account {AccountId}", symbol, systemAccountId);
                return false;
            }
        }

        public async Task<bool> DeductAssetAsync(int systemAccountId, string symbol, decimal amount, bool fromFrozen = true)
        {
            try
            {
                var asset = await GetSystemAssetAsync(systemAccountId, symbol);
                if (asset == null)
                {
                    return false;
                }

                if (fromFrozen && asset.Frozen >= amount)
                {
                    asset.Frozen -= amount;
                }
                else if (!fromFrozen && asset.Available >= amount)
                {
                    asset.Available -= amount;
                }
                else
                {
                    return false;
                }

                asset.Touch();
                await _assetRepository.UpdateAsync(asset);
                
                _logger.LogDebug("Deducted {Amount} {Symbol} from system account {AccountId} (fromFrozen={FromFrozen})", 
                    amount, symbol, systemAccountId, fromFrozen);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deducting asset {Symbol} for system account {AccountId}", symbol, systemAccountId);
                return false;
            }
        }

        public async Task<bool> AddAssetAsync(int systemAccountId, string symbol, decimal amount)
        {
            try
            {
                var asset = await GetSystemAssetAsync(systemAccountId, symbol);
                if (asset == null)
                {
                    // 如果资产不存在，创建它
                    await CreateSystemAssetAsync(systemAccountId, symbol, amount);
                    return true;
                }

                asset.Available += amount;
                asset.Touch();
                await _assetRepository.UpdateAsync(asset);
                
                _logger.LogDebug("Added {Amount} {Symbol} to system account {AccountId}", amount, symbol, systemAccountId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding asset {Symbol} for system account {AccountId}", symbol, systemAccountId);
                return false;
            }
        }

        public async Task<bool> HasSufficientBalanceAsync(int systemAccountId, string symbol, decimal amount)
        {
            try
            {
                var asset = await GetSystemAssetAsync(systemAccountId, symbol);
                return asset?.Available >= amount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking balance for {Symbol} in system account {AccountId}", symbol, systemAccountId);
                return false;
            }
        }

        public async Task InitializeSystemAssetsAsync(int systemAccountId, Dictionary<string, decimal> initialBalances)
        {
            try
            {
                foreach (var kvp in initialBalances)
                {
                    var existingAsset = await GetSystemAssetAsync(systemAccountId, kvp.Key);
                    if (existingAsset == null)
                    {
                        await CreateSystemAssetAsync(systemAccountId, kvp.Key, kvp.Value);
                    }
                }
                
                _logger.LogInformation("Initialized {Count} system assets for account {AccountId}", initialBalances.Count, systemAccountId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing system assets for account {AccountId}", systemAccountId);
                throw;
            }
        }

        public async Task<bool> AutoRefillAssetAsync(int systemAccountId, string symbol)
        {
            try
            {
                var asset = await GetSystemAssetAsync(systemAccountId, symbol);
                if (asset == null || !asset.AutoRefillEnabled || asset.Available >= asset.TargetBalance)
                {
                    return false;
                }

                var refillAmount = asset.TargetBalance - asset.Available;
                asset.Available = asset.TargetBalance;
                asset.Touch();
                
                await _assetRepository.UpdateAsync(asset);
                
                _logger.LogInformation("Auto refilled {Amount} {Symbol} for system account {AccountId}", refillAmount, symbol, systemAccountId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error auto refilling asset {Symbol} for system account {AccountId}", symbol, systemAccountId);
                return false;
            }
        }
    }
}
