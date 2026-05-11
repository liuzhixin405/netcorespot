using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.Auth;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.DTOs.Auth;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using static CryptoSpot.Domain.Entities.User;

namespace CryptoSpot.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly TimeProvider _timeProvider;
        private readonly IMemoryCache _cache;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IUserRepository userRepository,
            IPasswordHasher passwordHasher,
            ITokenService tokenService,
            TimeProvider timeProvider,
            IMemoryCache cache,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuthService> logger)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _timeProvider = timeProvider;
            _cache = cache;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<ApiResponseDto<AuthResultDto?>> LoginAsync(LoginRequest request)
        {
            var identifier = request.LoginIdentifier;
            if (string.IsNullOrEmpty(identifier))
                return ApiResponseDto<AuthResultDto?>.CreateError("用户名或邮箱不能为空");

            var user = identifier.Contains('@')
                ? await _userRepository.GetByEmailAsync(identifier)
                : await _userRepository.GetByUsernameAsync(identifier);

            if (user == null)
            {
                _logger.LogWarning("登录失败: 用户不存在 - {Identifier}", identifier);
                return ApiResponseDto<AuthResultDto?>.CreateError("用户名或密码错误");
            }

            if (string.IsNullOrEmpty(user.PasswordHash) || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            {
                _logger.LogWarning("登录失败: 密码错误 - UserId: {UserId}", user.Id);
                return ApiResponseDto<AuthResultDto?>.CreateError("用户名或密码错误");
            }

            await _userRepository.UpdateLastLoginAsync(user.Id);

            var token = _tokenService.GenerateToken(user.Id, user.Username);
            _logger.LogInformation("用户登录成功: UserId={UserId}, Username={Username}", user.Id, user.Username);

            return ApiResponseDto<AuthResultDto?>.CreateSuccess(new AuthResultDto
            {
                Token = token,
                ExpiresAt = _timeProvider.GetUtcNow().AddDays(7).UtcDateTime,
                User = MapToUserDto(user)
            });
        }

        public async Task<ApiResponseDto<AuthResultDto?>> RegisterAsync(RegisterRequest request)
        {
            if (await _userRepository.UsernameExistsAsync(request.Username))
                return ApiResponseDto<AuthResultDto?>.CreateError("用户名已存在");

            if (await _userRepository.EmailExistsAsync(request.Email))
                return ApiResponseDto<AuthResultDto?>.CreateError("邮箱已被注册");

            var passwordHash = _passwordHasher.Hash(request.Password);
            var nowTimestamp = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
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

            return ApiResponseDto<AuthResultDto?>.CreateSuccess(new AuthResultDto
            {
                Token = token,
                ExpiresAt = _timeProvider.GetUtcNow().AddDays(7).UtcDateTime,
                User = MapToUserDto(user)
            });
        }

        public async Task<ApiResponseDto<UserDto?>> GetCurrentUserAsync(long userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return ApiResponseDto<UserDto?>.CreateError("用户不存在");

            return ApiResponseDto<UserDto?>.CreateSuccess(MapToUserDto(user));
        }

        public async Task<ApiResponseDto<bool>> ValidateTokenAsync(string token)
        {
            var (isValid, userId, _) = _tokenService.ValidateToken(token);
            if (!isValid)
                return ApiResponseDto<bool>.CreateError("Token 无效或已过期");

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return ApiResponseDto<bool>.CreateError("用户不存在");

            return ApiResponseDto<bool>.CreateSuccess(true);
        }

        public Task<ApiResponseDto<bool>> LogoutAsync(long userId)
        {
            _logger.LogInformation("用户登出: UserId={UserId}", userId);

            var authHeader = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader["Bearer ".Length..].Trim();
                try
                {
                    var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                    var remainingLifetime = jwt.ValidTo - _timeProvider.GetUtcNow().UtcDateTime;
                    if (remainingLifetime > TimeSpan.Zero)
                    {
                        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
                        _cache.Set($"blacklist:{tokenHash}", true, remainingLifetime);
                    }
                }
                catch
                {
                    // Token format invalid, skip blacklisting
                }
            }

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
