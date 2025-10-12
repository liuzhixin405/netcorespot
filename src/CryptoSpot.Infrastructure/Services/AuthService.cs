using CryptoSpot.Application.Abstractions.Services.Auth;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.DTOs.Auth;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CryptoSpot.Application.Abstractions.Repositories;

namespace CryptoSpot.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAssetService _assetService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthService> _logger;
        private readonly IDtoMappingService _mappingService;

        public AuthService(
            IUserRepository userRepository,
            IAssetService assetService,
            IConfiguration configuration,
            ILogger<AuthService> logger,
            IDtoMappingService mappingService)
        {
            _userRepository = userRepository;
            _assetService = assetService;
            _configuration = configuration;
            _logger = logger;
            _mappingService = mappingService;
        }

        public async Task<ApiResponseDto<AuthResultDto?>> LoginAsync(LoginRequest request)
        {
            try
            {
                var user = (await _userRepository.FindAsync(u => u.Username == request.Username)).FirstOrDefault();
                if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash))
                {
                    _logger.LogWarning("登录失败: {Username}", request.Username);
                    return ApiResponseDto<AuthResultDto?>.CreateError("用户名或密码错误");
                }

                var verifyResult = VerifyPassword(request.Password, user.PasswordHash);
                if (!verifyResult.IsValid)
                {
                    _logger.LogWarning("登录失败: {Username}", request.Username);
                    return ApiResponseDto<AuthResultDto?>.CreateError("用户名或密码错误");
                }

                user.LastLoginAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _userRepository.UpdateAsync(user);

                var token = GenerateJwtToken(user);
                var dto = new AuthResultDto
                {
                    Token = token,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    User = _mappingService.MapToDto(user)
                };
                return ApiResponseDto<AuthResultDto?>.CreateSuccess(dto, "登录成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "登录异常: {Username}", request.Username);
                return ApiResponseDto<AuthResultDto?>.CreateError("登录失败");
            }
        }

        public async Task<ApiResponseDto<AuthResultDto?>> RegisterAsync(RegisterRequest request)
        {
            try
            {
                if ((await _userRepository.FindAsync(u => u.Username == request.Username)).Any())
                    return ApiResponseDto<AuthResultDto?>.CreateError("用户名已存在");
                if ((await _userRepository.FindAsync(u => u.Email == request.Email)).Any())
                    return ApiResponseDto<AuthResultDto?>.CreateError("邮箱已存在");

                var user = new Domain.Entities.User
                {
                    Username = request.Username.Trim().ToLowerInvariant(),
                    Email = request.Email.Trim().ToLowerInvariant(),
                    PasswordHash = HashPassword(request.Password),
                    IsActive = true
                };
                var created = await _userRepository.AddAsync(user);

                var initialBalances = new Dictionary<string, decimal>
                {
                    { "USDT", 10000m },
                    { "BTC", 0m },
                    { "ETH", 0m },
                    { "SOL", 0m }
                };
                await _assetService.InitializeUserAssetsAsync(created.Id, initialBalances);

                var token = GenerateJwtToken(created);
                var dto = new AuthResultDto
                {
                    Token = token,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    User = _mappingService.MapToDto(created)
                };
                return ApiResponseDto<AuthResultDto?>.CreateSuccess(dto, "注册成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册异常: {Username}", request.Username);
                return ApiResponseDto<AuthResultDto?>.CreateError("注册失败");
            }
        }

        public async Task<ApiResponseDto<UserDto?>> GetCurrentUserAsync(int userId)
        {
            try
            {
                var user = await _userRepository.GetByIdAsync(userId);
                return ApiResponseDto<UserDto?>.CreateSuccess(user != null ? _mappingService.MapToDto(user) : null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取当前用户异常 {UserId}", userId);
                return ApiResponseDto<UserDto?>.CreateError("获取用户失败");
            }
        }

        public Task<ApiResponseDto<bool>> ValidateTokenAsync(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_configuration["Jwt:SecretKey"] ?? _configuration["JwtSettings:SecretKey"] ?? "default-secret-key-32-length-!@#");
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out _);
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(true));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token校验失败");
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "Token无效"));
            }
        }

        public Task<ApiResponseDto<bool>> LogoutAsync(int userId)
        {
            _logger.LogInformation("用户退出: {UserId}", userId);
            return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(true, "已退出"));
        }

        #region 密码 & Token
        private const string PasswordAlgo = "PBKDF2-SHA256";
        private const int PasswordIterations = 600000;
        private const int SaltSize = 16;
        private const int KeySize = 32;

        private string HashPassword(string password)
        {
            var saltBytes = new byte[SaltSize];
            RandomNumberGenerator.Fill(saltBytes);
            var keyBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, PasswordIterations, HashAlgorithmName.SHA256, KeySize);
            var saltB64 = Convert.ToBase64String(saltBytes);
            var keyB64 = Convert.ToBase64String(keyBytes);
            return string.Join('$', PasswordAlgo, PasswordIterations.ToString(), saltB64, keyB64);
        }

        private (bool IsValid, bool NeedsUpgrade, string? UpgradedHash) VerifyPassword(string password, string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return (false, false, null);
            try
            {
                if (stored.Contains('$'))
                {
                    var parts = stored.Split('$');
                    if (parts.Length != 4) return (false, false, null);
                    var algo = parts[0];
                    if (!string.Equals(algo, PasswordAlgo, StringComparison.Ordinal)) return (false, false, null);
                    if (!int.TryParse(parts[1], out var iter) || iter <= 0) return (false, false, null);
                    var saltBytes = Convert.FromBase64String(parts[2]);
                    var storedKey = Convert.FromBase64String(parts[3]);
                    var derived = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, iter, HashAlgorithmName.SHA256, storedKey.Length);
                    var isValid = CryptographicOperations.FixedTimeEquals(derived, storedKey);
                    var needsUpgrade = isValid && iter != PasswordIterations;
                    string? upgraded = null;
                    if (needsUpgrade)
                    {
                        var newKey = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, PasswordIterations, HashAlgorithmName.SHA256, KeySize);
                        upgraded = string.Join('$', PasswordAlgo, PasswordIterations.ToString(), Convert.ToBase64String(saltBytes), Convert.ToBase64String(newKey));
                    }
                    return (isValid, needsUpgrade, upgraded);
                }
                else
                {
                    byte[] raw;
                    try { raw = Convert.FromBase64String(stored); } catch { return (false, false, null); }
                    if (raw.Length != SaltSize + KeySize) return (false, false, null);
                    var salt = new byte[SaltSize];
                    Buffer.BlockCopy(raw, 0, salt, 0, SaltSize);
                    var oldHash = new byte[KeySize];
                    Buffer.BlockCopy(raw, SaltSize, oldHash, 0, KeySize);
                    var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, KeySize);
                    var isValid = CryptographicOperations.FixedTimeEquals(derived, oldHash);
                    if (!isValid) return (false, false, null);
                    var upgraded = string.Join('$', PasswordAlgo, PasswordIterations.ToString(), Convert.ToBase64String(salt), Convert.ToBase64String(oldHash));
                    return (true, true, upgraded);
                }
            }
            catch
            {
                return (false, false, null);
            }
        }

        private string GenerateJwtToken(Domain.Entities.User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKeyString = jwtSettings["SecretKey"] ?? _configuration["Jwt:SecretKey"] ?? "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";
            var issuer = jwtSettings["Issuer"] ?? "CryptoSpot";
            var audience = jwtSettings["Audience"] ?? "CryptoSpotUsers";
            var key = Encoding.UTF8.GetBytes(secretKeyString);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username ?? string.Empty),
                    new Claim(ClaimTypes.Email, user.Email ?? string.Empty)
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
        #endregion
    }
}
