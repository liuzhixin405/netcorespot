using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.System;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class SystemAccountService : ISystemAccountService
    {
        private readonly IRepository<User> _userRepository;
        private readonly ILogger<SystemAccountService> _logger;

        public SystemAccountService(
            IRepository<User> userRepository,
            ILogger<SystemAccountService> logger)
        {
            _userRepository = userRepository;
            _logger = logger;
        }

        public async Task<User?> GetSystemAccountAsync(int id)
        {
            try
            {
                var user = await _userRepository.GetByIdAsync(id);
                return user?.Type != UserType.Regular ? user : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system account {Id}", id);
                return null;
            }
        }

        public async Task<IEnumerable<User>> GetSystemAccountsByTypeAsync(UserType type)
        {
            try
            {
                return await _userRepository.FindAsync(u => u.Type == type && u.Type != UserType.Regular);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system accounts by type {Type}", type);
                return new List<User>();
            }
        }

        public async Task<IEnumerable<User>> GetActiveSystemAccountsAsync()
        {
            try
            {
                return await _userRepository.FindAsync(u => u.Type != UserType.Regular && u.IsActive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active system accounts");
                return new List<User>();
            }
        }

        public async Task<User> CreateSystemAccountAsync(string name, UserType type, string description = "")
        {
            try
            {
                var systemAccount = new User
                {
                    Username = name,
                    Type = type,
                    Description = description,
                    IsActive = true,
                    IsAutoTradingEnabled = false,
                    MaxRiskRatio = 0.1m, // 默认最大风险比例10%
                    DailyTradingLimit = 100000m, // 默认每日交易限额
                    DailyTradedAmount = 0m,
                };

                var createdAccount = await _userRepository.AddAsync(systemAccount);
                _logger.LogInformation("Created system account {Name} of type {Type}", name, type);

                return createdAccount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating system account {Name}", name);
                throw;
            }
        }

        public async Task<User> UpdateSystemAccountAsync(User account)
        {
            try
            {
                account.Touch();
                await _userRepository.UpdateAsync(account);
                
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
                    throw new InvalidOperationException($"System account {accountId} not found");
                }

                account.IsAutoTradingEnabled = enabled;
                await UpdateSystemAccountAsync(account);
                
                _logger.LogInformation("Set auto trading status for system account {Id} to {Enabled}", accountId, enabled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting auto trading status for system account {Id}", accountId);
                throw;
            }
        }

        public async Task ResetDailyStatsAsync(int accountId)
        {
            try
            {
                var account = await GetSystemAccountAsync(accountId);
                if (account == null)
                {
                    throw new InvalidOperationException($"System account {accountId} not found");
                }

                account.DailyTradedAmount = 0m;
                await UpdateSystemAccountAsync(account);
                
                _logger.LogInformation("Reset daily stats for system account {Id}", accountId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting daily stats for system account {Id}", accountId);
                throw;
            }
        }
    }
}
