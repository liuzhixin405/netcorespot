using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 用户应用服务 - 协调用户相关的用例
    /// </summary>
    public class UserApplicationService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAssetRepository _assetRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<UserApplicationService> _logger;

        public UserApplicationService(
            IUserRepository userRepository,
            IAssetRepository assetRepository,
            IUnitOfWork unitOfWork,
            ILogger<UserApplicationService> logger)
        {
            _userRepository = userRepository;
            _assetRepository = assetRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <summary>
        /// 用户注册用例
        /// </summary>
        public async Task<User> RegisterUserAsync(string username, string email, string password)
        {
            // 1. 验证用户名和邮箱是否已存在
            if (await _userRepository.UsernameExistsAsync(username))
            {
                throw new ArgumentException("用户名已存在");
            }

            if (await _userRepository.EmailExistsAsync(email))
            {
                throw new ArgumentException("邮箱已存在");
            }

            // 2. 创建用户
            var user = new User
            {
                Username = username,
                Email = email,
                PasswordHash = HashPassword(password), // 实际项目中应该使用更安全的哈希
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // 3. 保存用户并初始化资产
            var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var createdUser = await _userRepository.AddAsync(user);
                
                // 初始化用户资产
                await InitializeUserAssetsAsync(createdUser.Id);
                
                await _unitOfWork.CommitTransactionAsync(transaction);
                
                _logger.LogInformation("用户注册成功: {Username}, {Email}", username, email);
                return createdUser;
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(transaction);
                _logger.LogError(ex, "用户注册失败: {Username}", username);
                throw;
            }
        }

        /// <summary>
        /// 用户登录用例
        /// </summary>
        public async Task<User?> LoginAsync(string username, string password)
        {
            var user = await _userRepository.ValidateCredentialsAsync(username, password);
            if (user != null)
            {
                await _userRepository.UpdateLastLoginAsync(user.Id);
                _logger.LogInformation("用户登录成功: {Username}", username);
            }
            return user;
        }

        /// <summary>
        /// 获取用户资产用例
        /// </summary>
        public async Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId)
        {
            return await _assetRepository.GetAssetsByUserIdAsync(userId);
        }

        /// <summary>
        /// 更新用户资产用例
        /// </summary>
        public async Task<bool> UpdateUserAssetAsync(int userId, string symbol, decimal amount)
        {
            var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var asset = await _assetRepository.GetUserAssetAsync(userId, symbol);
                if (asset == null)
                {
                    // 创建新资产
                    asset = new Asset
                    {
                        UserId = userId,
                        Symbol = symbol,
                        Available = amount,
                        Frozen = 0,
                        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    await _assetRepository.AddAsync(asset);
                }
                else
                {
                    // 更新现有资产
                    asset.Available = amount;
                    asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    await _assetRepository.UpdateAsync(asset);
                }

                await _unitOfWork.CommitTransactionAsync(transaction);
                return true;
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(transaction);
                _logger.LogError(ex, "更新用户资产失败: UserId={UserId}, Symbol={Symbol}", userId, symbol);
                return false;
            }
        }

        private async Task InitializeUserAssetsAsync(int userId)
        {
            // 初始化常用资产
            var defaultAssets = new[] { "USDT", "BTC", "ETH" };
            
            foreach (var symbol in defaultAssets)
            {
                var asset = new Asset
                {
                    UserId = userId,
                    Symbol = symbol,
                    Available = symbol == "USDT" ? 10000 : 0, // 给新用户10000 USDT
                    Frozen = 0,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                await _assetRepository.AddAsync(asset);
            }
        }

        private string HashPassword(string password)
        {
            // 实际项目中应该使用BCrypt或其他安全的哈希算法
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
        }
    }
}
