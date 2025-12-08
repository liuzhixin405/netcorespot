using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.Auth;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.DTOs.Auth;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;
using static CryptoSpot.Domain.Entities.User;

namespace CryptoSpot.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IUserRepository userRepository,
            IPasswordHasher passwordHasher,
            ITokenService tokenService,
            ILogger<AuthService> logger)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _logger = logger;
        }

        public Task<ApiResponseDto<AuthResultDto?>> LoginAsync(LoginRequest request)
        {
            return ServiceHelper.ExecuteAsync<AuthResultDto?>(async () =>
            {
                var identifier = request.LoginIdentifier;
                if (string.IsNullOrEmpty(identifier))
                    throw new InvalidOperationException("用户名或邮箱不能为空");
                
                var user = identifier.Contains('@') 
                    ? await _userRepository.GetByEmailAsync(identifier)
                    : await _userRepository.GetByUsernameAsync(identifier);

                if (user == null)
                {
                    _logger.LogWarning("登录失败: 用户不存在 - {Identifier}", identifier);
                    throw new InvalidOperationException("用户名或密码错误");
                }

                if (string.IsNullOrEmpty(user.PasswordHash) || !_passwordHasher.Verify(request.Password, user.PasswordHash))
                {
                    _logger.LogWarning("登录失败: 密码错误 - UserId: {UserId}", user.Id);
                    throw new InvalidOperationException("用户名或密码错误");
                }

                await _userRepository.UpdateLastLoginAsync(user.Id);

                var token = _tokenService.GenerateToken(user.Id, user.Username);
                _logger.LogInformation("用户登录成功: UserId={UserId}, Username={Username}", user.Id, user.Username);
                
                return new AuthResultDto
                {
                    Token = token,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    User = MapToUserDto(user)
                };
            }, _logger, "登录失败，请稍后重试");
        }

        public Task<ApiResponseDto<AuthResultDto?>> RegisterAsync(RegisterRequest request)
        {
            return ServiceHelper.ExecuteAsync<AuthResultDto?>(async () =>
            {
                if (await _userRepository.UsernameExistsAsync(request.Username))
                    throw new InvalidOperationException("用户名已存在");

                if (await _userRepository.EmailExistsAsync(request.Email))
                    throw new InvalidOperationException("邮箱已被注册");

                var passwordHash = _passwordHasher.Hash(request.Password);
                var nowTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var user = new User
                {
                    Username = request.Username,
                    Email = request.Email,
                    PasswordHash = passwordHash,
                    Type = UserType.Regular,
                    IsAutoTradingEnabled = false,
                    MaxRiskRatio = 0.1m,
                    DailyTradingLimit = 10000m,
                    DailyTradedAmount = 0m,
                    IsActive = true,
                    CreatedAt = nowTimestamp,
                    UpdatedAt = nowTimestamp
                };

                await _userRepository.AddAsync(user);

                var token = _tokenService.GenerateToken(user.Id, user.Username);
                _logger.LogInformation("用户注册成功: UserId={UserId}, Username={Username}, Email={Email}", 
                    user.Id, user.Username, user.Email);
                
                return new AuthResultDto
                {
                    Token = token,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    User = MapToUserDto(user)
                };
            }, _logger, "注册失败，请稍后重试");
        }

        public Task<ApiResponseDto<UserDto?>> GetCurrentUserAsync(long userId)
        {
            return ServiceHelper.ExecuteAsync<UserDto?>(async () =>
            {
                var user = await _userRepository.GetByIdAsync(userId) 
                    ?? throw new InvalidOperationException("用户不存在");
                return MapToUserDto(user);
            }, _logger, "获取用户信息失败");
        }

        public Task<ApiResponseDto<bool>> ValidateTokenAsync(string token)
        {
            return ServiceHelper.ExecuteAsync(async () =>
            {
                var (isValid, userId, _) = _tokenService.ValidateToken(token);
                if (!isValid) throw new InvalidOperationException("Token 无效或已过期");

                var user = await _userRepository.GetByIdAsync(userId) 
                    ?? throw new InvalidOperationException("用户不存在");
                return true;
            }, _logger, "Token 验证失败");
        }

        public Task<ApiResponseDto<bool>> LogoutAsync(long userId)
        {
            // JWT 是无状态的，客户端直接删除 token 即可
            _logger.LogInformation("用户登出: UserId={UserId}", userId);
            return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(true));
        }

        private UserDto MapToUserDto(User user)
        {
            return new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Type = (UserTypeDto)(int)user.Type,
                Description = user.Description,
                IsAutoTradingEnabled = user.IsAutoTradingEnabled,
                MaxRiskRatio = user.MaxRiskRatio,
                DailyTradingLimit = user.DailyTradingLimit,
                DailyTradedAmount = user.DailyTradedAmount,
                IsSystemAccount = user.Type != UserType.Regular,
                LastLoginAt = user.LastLoginAt.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(user.LastLoginAt.Value).DateTime : null,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(user.CreatedAt).DateTime,
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(user.UpdatedAt).DateTime
            };
        }
    }
}
