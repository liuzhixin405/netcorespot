using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.System;
using CryptoSpot.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class SystemAccountService : ISystemAccountService
    {
        private readonly IRepository<SystemAccount> _systemAccountRepository;
        private readonly ILogger<SystemAccountService> _logger;

        public SystemAccountService(
            IRepository<SystemAccount> systemAccountRepository,
            ILogger<SystemAccountService> logger)
        {
            _systemAccountRepository = systemAccountRepository;
            _logger = logger;
        }

        public async Task<SystemAccount?> GetSystemAccountAsync(int id)
        {
            try
            {
                return await _systemAccountRepository.GetByIdAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system account {Id}", id);
                return null;
            }
        }

        public async Task<IEnumerable<SystemAccount>> GetSystemAccountsByTypeAsync(SystemAccountType type)
        {
            try
            {
                return await _systemAccountRepository.FindAsync(a => a.Type == type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system accounts by type {Type}", type);
                return new List<SystemAccount>();
            }
        }

        public async Task<IEnumerable<SystemAccount>> GetActiveSystemAccountsAsync()
        {
            try
            {
                return await _systemAccountRepository.FindAsync(a => a.IsActive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active system accounts");
                return new List<SystemAccount>();
            }
        }

        public async Task<SystemAccount> CreateSystemAccountAsync(string name, SystemAccountType type, string description = "")
        {
            try
            {
                var systemAccount = new SystemAccount
                {
                    Name = name,
                    Type = type,
                    Description = description,
                    IsActive = true,
                    IsAutoTradingEnabled = false,
                    MaxRiskRatio = 0.1m, // 默认最大风险比例10%
                    DailyTradingLimit = 100000m, // 默认每日交易限额
                    DailyTradedAmount = 0m,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                var createdAccount = await _systemAccountRepository.AddAsync(systemAccount);
                _logger.LogInformation("Created system account {Name} of type {Type}", name, type);

                return createdAccount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating system account {Name}", name);
                throw;
            }
        }

        public async Task<SystemAccount> UpdateSystemAccountAsync(SystemAccount account)
        {
            try
            {
                account.UpdatedAt = DateTime.UtcNow;
                await _systemAccountRepository.UpdateAsync(account);
                
                _logger.LogInformation("Updated system account {Id}", account.Id);
                return account;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating system account {Id}", account.Id);
                throw;
            }
        }

        public async Task SetAutoTradingStatusAsync(int accountId, bool enabled)
        {
            try
            {
                var account = await GetSystemAccountAsync(accountId);
                if (account == null)
                {
                    _logger.LogWarning("System account {AccountId} not found", accountId);
                    return;
                }

                account.IsAutoTradingEnabled = enabled;
                await UpdateSystemAccountAsync(account);
                
                _logger.LogInformation("Set auto trading {Status} for system account {AccountId}", 
                    enabled ? "enabled" : "disabled", accountId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting auto trading status for account {AccountId}", accountId);
            }
        }

        public async Task ResetDailyStatsAsync(int accountId)
        {
            try
            {
                var account = await GetSystemAccountAsync(accountId);
                if (account == null)
                {
                    _logger.LogWarning("System account {AccountId} not found", accountId);
                    return;
                }

                account.DailyTradedAmount = 0m;
                await UpdateSystemAccountAsync(account);
                
                _logger.LogInformation("Reset daily stats for system account {AccountId}", accountId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting daily stats for account {AccountId}", accountId);
            }
        }
    }
}
