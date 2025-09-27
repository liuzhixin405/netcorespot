using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Users; // migrated from Core.Interfaces.Users
using CryptoSpot.Application.Abstractions.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 用户服务实现 - 使用Redis缓存
    /// </summary>
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly RedisCacheService _cacheService;
        private readonly ILogger<UserService> _logger;

        public UserService(
            IUserRepository userRepository,
            RedisCacheService cacheService,
            ILogger<UserService> logger)
        {
            _userRepository = userRepository;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            try
            {
                // 先从Redis缓存获取
                var cachedUser = await _cacheService.GetUserAsync(userId);
                if (cachedUser != null)
                {
                    return cachedUser;
                }

                // 缓存中没有，从数据库获取
                var user = await _userRepository.GetByIdAsync(userId);
                if (user != null)
                {
                    // 缓存到Redis
                    await _cacheService.SetUserAsync(user);
                }

                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户失败: UserId={UserId}", userId);
                return null;
            }
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            try
            {
                // 直接从数据库查找（用户名查找频率较低，不必复杂化缓存逻辑）
                var user = (await _userRepository.FindAsync(u => u.Username == username)).FirstOrDefault();
                
                if (user != null)
                {
                    // 缓存到Redis
                    await _cacheService.SetUserAsync(user);
                }
                
                return user;
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
                // 直接从数据库查找（邮箱查找频率较低，不必复杂化缓存逻辑）
                var user = (await _userRepository.FindAsync(u => u.Email == email)).FirstOrDefault();
                
                if (user != null)
                {
                    // 缓存到Redis
                    await _cacheService.SetUserAsync(user);
                }
                
                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查找用户失败: Email={Email}", email);
                return null;
            }
        }

        public async Task<IEnumerable<User>> GetSystemUsersAsync()
        {
            try
            {
                // 系统用户查询直接从数据库获取
                return await _userRepository.FindAsync(u => u.Type != UserType.Regular);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取系统用户失败");
                return Enumerable.Empty<User>();
            }
        }

        public async Task<IEnumerable<User>> GetActiveSystemUsersAsync()
        {
            try
            {
                // 活跃系统用户查询直接从数据库获取
                return await _userRepository.FindAsync(u => u.Type != UserType.Regular && u.IsActive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃系统用户失败");
                return Enumerable.Empty<User>();
            }
        }

        public async Task<User> CreateUserAsync(User user)
        {
            try
            {
                user.Touch(); // 设置创建和更新时间
                var createdUser = await _userRepository.AddAsync(user);
                
                // 缓存到Redis
                await _cacheService.SetUserAsync(createdUser);
                
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
                
                // 更新Redis缓存
                await _cacheService.SetUserAsync(user);
                
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
                    
                    // 从Redis缓存中移除
                    await _cacheService.RemoveUserAsync(userId);
                    
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
