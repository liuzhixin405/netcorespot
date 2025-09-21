using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Auth;
using CryptoSpot.Core.Interfaces.Users;
using CryptoSpot.Core.Commands.Auth;
using CryptoSpot.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserService _userService;
        private readonly IAssetService _assetService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IUserService userService,
            IAssetService assetService,
            IConfiguration configuration,
            ILogger<AuthService> logger)
        {
            _userService = userService;
            _assetService = assetService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<AuthResult?> LoginAsync(LoginCommand command)
        {
            try
            {
                var user = await _userService.GetUserByUsernameAsync(command.Username);
                if (user == null)
                {
                    _logger.LogWarning("Login attempt with non-existent username: {Username}", command.Username);
                    return null;
                }

                if (!VerifyPassword(command.Password, user.PasswordHash))
                {
                    _logger.LogWarning("Login attempt with invalid password for user: {Username}", command.Username);
                    return null;
                }

                // Update last login time
                user.LastLoginAt = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();
                await _userService.UpdateUserAsync(user);

                var token = GenerateJwtToken(user);

                return new AuthResult
                {
                    Token = token,
                    Username = user.Username,
                    Email = user.Email,
                    ExpiresAt = DateTime.UtcNow.AddDays(7) // 7天
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user: {Username}", command.Username);
                return null;
            }
        }

        public async Task<AuthResult?> RegisterAsync(RegisterCommand command)
        {
            try
            {
                // Check if username already exists
                var existingUserByUsername = await _userService.GetUserByUsernameAsync(command.Username);
                if (existingUserByUsername != null)
                {
                    _logger.LogWarning("Registration attempt with existing username: {Username}", command.Username);
                    return null;
                }

                // Check if email already exists
                var existingUserByEmail = await _userService.GetUserByEmailAsync(command.Email);
                if (existingUserByEmail != null)
                {
                    _logger.LogWarning("Registration attempt with existing email: {Email}", command.Email);
                    return null;
                }

                var user = new User
                {
                    Username = command.Username,
                    Email = command.Email,
                    PasswordHash = HashPassword(command.Password),
                    IsActive = true
                };

                var createdUser = await _userService.CreateUserAsync(user);

                // Initialize user assets
                var initialBalances = new Dictionary<string, decimal>
                {
                    { "USDT", 10000m }, // Give new users 10,000 USDT for testing
                    { "BTC", 0m },
                    { "ETH", 0m },
                    { "SOL", 0m }
                };
                
                await _assetService.InitializeUserAssetsAsync(createdUser.Id, initialBalances);
                _logger.LogInformation("Initialized assets for new user {UserId}", createdUser.Id);

                var token = GenerateJwtToken(createdUser);

                return new AuthResult
                {
                    Token = token,
                    Username = createdUser.Username,
                    Email = createdUser.Email,
                    ExpiresAt = DateTime.UtcNow.AddDays(7)// 7天
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for user: {Username}", command.Username);
                return null;
            }
        }

        public async Task<User?> GetCurrentUserAsync(int userId)
        {
            try
            {
                return await _userService.GetUserByIdAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user: {UserId}", userId);
                return null;
            }
        }

        public async Task<bool> ValidateTokenAsync(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_configuration["Jwt:SecretKey"] ?? "default-secret-key");

                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                return validatedToken != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token validation failed");
                return false;
            }
        }

        public async Task LogoutAsync(int userId)
        {
            // In a real application, you might want to blacklist the token
            // For now, we'll just log the logout
            _logger.LogInformation("User {UserId} logged out", userId);
        }

        private string HashPassword(string password)
        {
            using var rng = RandomNumberGenerator.Create();
            var saltBytes = new byte[16];
            rng.GetBytes(saltBytes);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 10000);
            var hashBytes = pbkdf2.GetBytes(32);

            var hashWithSalt = new byte[48];
            Array.Copy(saltBytes, 0, hashWithSalt, 0, 16);
            Array.Copy(hashBytes, 0, hashWithSalt, 16, 32);

            return Convert.ToBase64String(hashWithSalt);
        }

        private bool VerifyPassword(string password, string storedHash)
        {
            try
            {
                var hashWithSalt = Convert.FromBase64String(storedHash);
                var saltBytes = new byte[16];
                Array.Copy(hashWithSalt, 0, saltBytes, 0, 16);

                using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 10000);
                var hashBytes = pbkdf2.GetBytes(32);

                for (int i = 0; i < 32; i++)
                {
                    if (hashWithSalt[i + 16] != hashBytes[i])
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKeyString = jwtSettings["SecretKey"] ?? "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";
            var issuer = jwtSettings["Issuer"] ?? "CryptoSpot";
            var audience = jwtSettings["Audience"] ?? "CryptoSpotUsers";
            var key = Encoding.UTF8.GetBytes(secretKeyString);
            
            _logger.LogInformation("JWT Key length: {KeyLength} bytes ({BitLength} bits)", key.Length, key.Length * 8);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Email, user.Email)
                }),
                Expires = DateTime.UtcNow.AddDays(7), // 7天
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
