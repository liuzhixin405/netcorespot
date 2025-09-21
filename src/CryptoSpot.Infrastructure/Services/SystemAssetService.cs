using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.System;
using CryptoSpot.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class SystemAssetService : ISystemAssetService
    {
        private readonly IRepository<SystemAsset> _systemAssetRepository;
        private readonly IRepository<SystemAccount> _systemAccountRepository;
        private readonly ILogger<SystemAssetService> _logger;

        public SystemAssetService(
            IRepository<SystemAsset> systemAssetRepository,
            IRepository<SystemAccount> systemAccountRepository,
            ILogger<SystemAssetService> logger)
        {
            _systemAssetRepository = systemAssetRepository;
            _systemAccountRepository = systemAccountRepository;
            _logger = logger;
        }

        public async Task<IEnumerable<SystemAsset>> GetSystemAssetsAsync(int systemAccountId)
        {
            try
            {
                return await _systemAssetRepository.FindAsync(a => a.SystemAccountId == systemAccountId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system assets for account {SystemAccountId}", systemAccountId);
                return new List<SystemAsset>();
            }
        }

        public async Task<SystemAsset?> GetSystemAssetAsync(int systemAccountId, string symbol)
        {
            try
            {
                return await _systemAssetRepository.FirstOrDefaultAsync(a => 
                    a.SystemAccountId == systemAccountId && a.Symbol == symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system asset {Symbol} for account {SystemAccountId}", symbol, systemAccountId);
                return null;
            }
        }

        public async Task<SystemAsset> CreateSystemAssetAsync(int systemAccountId, string symbol, decimal initialBalance = 0)
        {
            try
            {
                var systemAsset = new SystemAsset
                {
                    SystemAccountId = systemAccountId,
                    Symbol = symbol,
                    Available = initialBalance,
                    Frozen = 0,
                    MinReserve = 0,
                    TargetBalance = initialBalance,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                var createdAsset = await _systemAssetRepository.AddAsync(systemAsset);
                _logger.LogInformation("Created system asset {Symbol} for account {SystemAccountId} with balance {Balance}", 
                    symbol, systemAccountId, initialBalance);

                return createdAsset;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating system asset {Symbol} for account {SystemAccountId}", symbol, systemAccountId);
                throw;
            }
        }

        public async Task<SystemAsset> UpdateAssetBalanceAsync(int systemAccountId, string symbol, decimal available, decimal frozen)
        {
            try
            {
                var asset = await GetSystemAssetAsync(systemAccountId, symbol);
                if (asset == null)
                {
                    asset = await CreateSystemAssetAsync(systemAccountId, symbol, available);
                    asset.Frozen = frozen;
                    await _systemAssetRepository.UpdateAsync(asset);
                }
                else
                {
                    asset.Available = available;
                    asset.Frozen = frozen;
                    asset.UpdatedAt = DateTime.UtcNow;
                    await _systemAssetRepository.UpdateAsync(asset);
                }

                return asset;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating system asset balance {Symbol} for account {SystemAccountId}", symbol, systemAccountId);
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
                    _logger.LogWarning("Insufficient system balance to freeze {Amount} {Symbol} for account {SystemAccountId}", 
                        amount, symbol, systemAccountId);
                    return false;
                }

                asset.Available -= amount;
                asset.Frozen += amount;
                asset.UpdatedAt = DateTime.UtcNow;
                
                await _systemAssetRepository.UpdateAsync(asset);
                
                _logger.LogInformation("Froze {Amount} {Symbol} for system account {SystemAccountId}", amount, symbol, systemAccountId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error freezing system asset {Symbol} for account {SystemAccountId}", symbol, systemAccountId);
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
                    _logger.LogWarning("Insufficient frozen system balance to unfreeze {Amount} {Symbol} for account {SystemAccountId}", 
                        amount, symbol, systemAccountId);
                    return false;
                }

                asset.Frozen -= amount;
                asset.Available += amount;
                asset.UpdatedAt = DateTime.UtcNow;
                
                await _systemAssetRepository.UpdateAsync(asset);
                
                _logger.LogInformation("Unfroze {Amount} {Symbol} for system account {SystemAccountId}", amount, symbol, systemAccountId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unfreezing system asset {Symbol} for account {SystemAccountId}", symbol, systemAccountId);
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
                    _logger.LogWarning("System asset {Symbol} not found for account {SystemAccountId}", symbol, systemAccountId);
                    return false;
                }

                if (fromFrozen)
                {
                    if (asset.Frozen < amount)
                    {
                        _logger.LogWarning("Insufficient frozen system balance to deduct {Amount} {Symbol} for account {SystemAccountId}", 
                            amount, symbol, systemAccountId);
                        return false;
                    }
                    asset.Frozen -= amount;
                }
                else
                {
                    if (asset.Available < amount)
                    {
                        _logger.LogWarning("Insufficient available system balance to deduct {Amount} {Symbol} for account {SystemAccountId}", 
                            amount, symbol, systemAccountId);
                        return false;
                    }
                    asset.Available -= amount;
                }

                asset.UpdatedAt = DateTime.UtcNow;
                await _systemAssetRepository.UpdateAsync(asset);
                
                _logger.LogInformation("Deducted {Amount} {Symbol} from system account {SystemAccountId} ({Source})", 
                    amount, symbol, systemAccountId, fromFrozen ? "frozen" : "available");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deducting system asset {Symbol} for account {SystemAccountId}", symbol, systemAccountId);
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
                    await CreateSystemAssetAsync(systemAccountId, symbol, amount);
                }
                else
                {
                    asset.Available += amount;
                    asset.UpdatedAt = DateTime.UtcNow;
                    await _systemAssetRepository.UpdateAsync(asset);
                }
                
                _logger.LogInformation("Added {Amount} {Symbol} to system account {SystemAccountId}", amount, symbol, systemAccountId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding system asset {Symbol} for account {SystemAccountId}", symbol, systemAccountId);
                return false;
            }
        }

        public async Task<bool> HasSufficientBalanceAsync(int systemAccountId, string symbol, decimal amount)
        {
            try
            {
                var asset = await GetSystemAssetAsync(systemAccountId, symbol);
                return asset != null && asset.Available >= amount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking system balance for account {SystemAccountId}, symbol {Symbol}", systemAccountId, symbol);
                return false;
            }
        }

        public async Task InitializeSystemAssetsAsync(int systemAccountId, Dictionary<string, decimal> initialBalances)
        {
            try
            {
                foreach (var balance in initialBalances)
                {
                    var existingAsset = await GetSystemAssetAsync(systemAccountId, balance.Key);
                    if (existingAsset == null)
                    {
                        await CreateSystemAssetAsync(systemAccountId, balance.Key, balance.Value);
                    }
                }
                
                _logger.LogInformation("Initialized system assets for account {SystemAccountId} with {Count} assets", 
                    systemAccountId, initialBalances.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing system assets for account {SystemAccountId}", systemAccountId);
                throw;
            }
        }

        public async Task<bool> AutoRefillAssetAsync(int systemAccountId, string symbol)
        {
            try
            {
                var asset = await GetSystemAssetAsync(systemAccountId, symbol);
                if (asset == null)
                {
                    return false;
                }

                // 如果当前余额低于目标余额，进行自动充值
                var totalBalance = asset.Available + asset.Frozen;
                if (totalBalance < asset.TargetBalance)
                {
                    var refillAmount = asset.TargetBalance - totalBalance;
                    await AddAssetAsync(systemAccountId, symbol, refillAmount);
                    
                    _logger.LogInformation("Auto-refilled {Amount} {Symbol} for system account {SystemAccountId}", 
                        refillAmount, symbol, systemAccountId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error auto-refilling system asset {Symbol} for account {SystemAccountId}", symbol, systemAccountId);
                return false;
            }
        }
    }
}
