using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Interfaces.Caching;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 用户服务实现
    /// </summary>
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly ICacheService _cacheService;
        private readonly ICacheEventService _cacheEventService;
        private readonly ILogger<UserService> _logger;

        public UserService(
            IUserRepository userRepository,
            ICacheService cacheService,
            ICacheEventService cacheEventService,
            ILogger<UserService> logger)
        {
            _userRepository = userRepository;
            _cacheService = cacheService;
            _cacheEventService = cacheEventService;
            _logger = logger;
        }

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            return await _cacheService.GetCachedUserByIdAsync(userId);
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            try
            {
                // 先从缓存查找
                var users = await _cacheService.GetCachedUsersAsync();
                var cachedUser = users.FirstOrDefault(u => u.Username == username);
                
                if (cachedUser != null)
                {
                    return cachedUser;
                }
                
                // 如果缓存中没有找到，直接从数据库查找以确保数据一致性
                _logger.LogDebug("缓存中未找到用户，从数据库查找: Username={Username}", username);
                var dbUser = (await _userRepository.FindAsync(u => u.Username == username)).FirstOrDefault();
                
                if (dbUser != null)
                {
                    _logger.LogWarning("⚠️ 用户存在于数据库但缓存中不存在: Username={Username}, UserId={UserId}", 
                        username, dbUser.Id);
                    
                    // 更新缓存
                    await _cacheEventService.NotifyUserChangedAsync(dbUser.Id);
                }
                
                return dbUser;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查找用户失败: Username={Username}", username);
                return null;
            }
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            try
            {
                // 先从缓存查找
                var users = await _cacheService.GetCachedUsersAsync();
                var cachedUser = users.FirstOrDefault(u => u.Email == email);
                
                if (cachedUser != null)
                {
                    return cachedUser;
                }
                
                // 如果缓存中没有找到，直接从数据库查找以确保数据一致性
                _logger.LogDebug("缓存中未找到用户，从数据库查找: Email={Email}", email);
                var dbUser = (await _userRepository.FindAsync(u => u.Email == email)).FirstOrDefault();
                
                if (dbUser != null)
                {
                    _logger.LogWarning("⚠️ 用户存在于数据库但缓存中不存在: Email={Email}, UserId={UserId}", 
                        email, dbUser.Id);
                    
                    // 更新缓存
                    await _cacheEventService.NotifyUserChangedAsync(dbUser.Id);
                }
                
                return dbUser;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查找用户失败: Email={Email}", email);
                return null;
            }
        }

        public async Task<IEnumerable<User>> GetSystemUsersAsync()
        {
            var users = await _cacheService.GetCachedUsersAsync();
            return users.Where(u => u.Type != UserType.Regular);
        }

        public async Task<IEnumerable<User>> GetActiveSystemUsersAsync()
        {
            var users = await _cacheService.GetCachedUsersAsync();
            return users.Where(u => u.Type != UserType.Regular && u.IsActive);
        }

        public async Task<User> CreateUserAsync(User user)
        {
            try
            {
                user.Touch(); // 设置创建和更新时间
                var createdUser = await _userRepository.AddAsync(user);
                
                // 通知缓存服务用户数据已变更
                await _cacheEventService.NotifyUserChangedAsync(createdUser.Id);
                
                _logger.LogInformation("用户创建成功: UserId={UserId}, Username={Username}", 
                    createdUser.Id, createdUser.Username);
                
                return createdUser;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建用户失败: Username={Username}", user.Username);
                throw;
            }
        }

        public async Task UpdateUserAsync(User user)
        {
            try
            {
                user.Touch(); // 更新时间戳
                 await _userRepository.UpdateAsync(user);
                
                // 通知缓存服务用户数据已变更
                await _cacheEventService.NotifyUserChangedAsync(user.Id);
                
                _logger.LogInformation("用户更新成功: UserId={UserId}, Username={Username}", 
                    user.Id, user.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户失败: UserId={UserId}", user.Id);
                throw;
            }
        }

        public async Task DeleteUserAsync(int userId)
        {
            try
            {
                var user = await _userRepository.GetByIdAsync(userId);
                if (user != null)
                {
                    await _userRepository.DeleteAsync(user);
                    
                    // 通知缓存服务用户数据已变更
                    await _cacheEventService.NotifyUserChangedAsync(userId);
                    
                    _logger.LogInformation("用户删除成功: UserId={UserId}, Username={Username}", 
                        userId, user.Username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除用户失败: UserId={UserId}", userId);
                throw;
            }
        }
    }
}
