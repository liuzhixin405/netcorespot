using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.System;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class SystemAccountService : ISystemAccountService
    {
        private readonly IUserService _userService;
        private readonly ILogger<SystemAccountService> _logger;

        public SystemAccountService(
            IUserService userService,
            ILogger<SystemAccountService> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        public async Task<User?> GetSystemAccountAsync(int id)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(id);
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
                var allUsers = await _userService.GetSystemUsersAsync();
                return allUsers.Where(u => u.Type == type);
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
                return await _userService.GetActiveSystemUsersAsync();
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

                var createdAccount = await _userService.CreateUserAsync(systemAccount);
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
                await _userService.UpdateUserAsync(account);
                
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
