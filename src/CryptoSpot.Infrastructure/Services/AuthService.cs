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

        public async Task<ApiResponseDto<AuthResultDto?>> LoginAsync(LoginRequest request)
        {
            try
            {
                // 通过用户名或邮箱查找用户
                var identifier = request.LoginIdentifier;
                if (string.IsNullOrEmpty(identifier))
                {
                    return ApiResponseDto<AuthResultDto?>.CreateError("用户名或邮箱不能为空");
                }
                
                User? user = null;
                
                if (identifier.Contains('@'))
                {
                    user = await _userRepository.GetByEmailAsync(identifier);
                }
                else
                {
                    user = await _userRepository.GetByUsernameAsync(identifier);
                }

                if (user == null)
                {
                    _logger.LogWarning("登录失败: 用户不存在 - {Identifier}", identifier);
                    return ApiResponseDto<AuthResultDto?>.CreateError("用户名或密码错误");
                }

                // 验证密码
                if (string.IsNullOrEmpty(user.PasswordHash) || !_passwordHasher.Verify(request.Password, user.PasswordHash))
                {
                    _logger.LogWarning("登录失败: 密码错误 - UserId: {UserId}", user.Id);
                    return ApiResponseDto<AuthResultDto?>.CreateError("用户名或密码错误");
                }

                // 更新最后登录时间
                await _userRepository.UpdateLastLoginAsync(user.Id);

                // 生成 Token
                var token = _tokenService.GenerateToken(user.Id, user.Username);
                var expiresAt = DateTime.UtcNow.AddDays(7); // 与 JwtTokenService 配置一致

                var authResult = new AuthResultDto
                {
                    Token = token,
                    ExpiresAt = expiresAt,
                    User = MapToUserDto(user)
                };

                _logger.LogInformation("用户登录成功: UserId={UserId}, Username={Username}", user.Id, user.Username);
                return ApiResponseDto<AuthResultDto?>.CreateSuccess(authResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "登录过程中发生错误: {Identifier}", request.LoginIdentifier);
                return ApiResponseDto<AuthResultDto?>.CreateError("登录失败，请稍后重试");
            }
        }

        public async Task<ApiResponseDto<AuthResultDto?>> RegisterAsync(RegisterRequest request)
        {
            try
            {
                // 检查用户名是否已存在
                if (await _userRepository.UsernameExistsAsync(request.Username))
                {
                    return ApiResponseDto<AuthResultDto?>.CreateError("用户名已存在");
                }

                // 检查邮箱是否已存在
                if (await _userRepository.EmailExistsAsync(request.Email))
                {
                    return ApiResponseDto<AuthResultDto?>.CreateError("邮箱已被注册");
                }

                // 创建新用户
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

                // 生成 Token
                var token = _tokenService.GenerateToken(user.Id, user.Username);
                var expiresAt = DateTime.UtcNow.AddDays(7);

                var authResult = new AuthResultDto
                {
                    Token = token,
                    ExpiresAt = expiresAt,
                    User = MapToUserDto(user)
                };

                _logger.LogInformation("用户注册成功: UserId={UserId}, Username={Username}, Email={Email}", 
                    user.Id, user.Username, user.Email);
                
                return ApiResponseDto<AuthResultDto?>.CreateSuccess(authResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册过程中发生错误: Username={Username}, Email={Email}", 
                    request.Username, request.Email);
                return ApiResponseDto<AuthResultDto?>.CreateError("注册失败，请稍后重试");
            }
        }

        public async Task<ApiResponseDto<UserDto?>> GetCurrentUserAsync(long userId)
        {
            try
            {
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null)
                {
                    return ApiResponseDto<UserDto?>.CreateError("用户不存在");
                }

                var userDto = MapToUserDto(user);
                return ApiResponseDto<UserDto?>.CreateSuccess(userDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户信息失败: UserId={UserId}", userId);
                return ApiResponseDto<UserDto?>.CreateError("获取用户信息失败");
            }
        }

        public async Task<ApiResponseDto<bool>> ValidateTokenAsync(string token)
        {
            try
            {
                var (isValid, userId, username) = _tokenService.ValidateToken(token);
                
                if (!isValid)
                {
                    return ApiResponseDto<bool>.CreateError("Token 无效或已过期");
                }

                // 验证用户是否仍然存在
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null)
                {
                    return ApiResponseDto<bool>.CreateError("用户不存在");
                }

                return ApiResponseDto<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token 验证失败");
                return ApiResponseDto<bool>.CreateError("Token 验证失败");
            }
        }

        public Task<ApiResponseDto<bool>> LogoutAsync(long userId)
        {
            try
            {
                // JWT 是无状态的，客户端直接删除 token 即可
                // 如果需要黑名单功能，可以在这里添加
                _logger.LogInformation("用户登出: UserId={UserId}", userId);
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "登出失败: UserId={UserId}", userId);
                return Task.FromResult(ApiResponseDto<bool>.CreateError("登出失败"));
            }
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
