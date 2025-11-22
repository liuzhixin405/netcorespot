using CryptoSpot.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CryptoSpot.Infrastructure.Identity
{
    /// <summary>
    /// JWT Token 服务实现
    /// </summary>
    public class JwtTokenService : ITokenService
    {
        private readonly string _secretKey;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _expirationDays;

        public JwtTokenService(IConfiguration configuration)
        {
            var jwtSettings = configuration.GetSection("JwtSettings");
            _secretKey = jwtSettings["SecretKey"] ?? "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";
            _issuer = jwtSettings["Issuer"] ?? "CryptoSpot";
            _audience = jwtSettings["Audience"] ?? "CryptoSpotUsers";
            _expirationDays = int.TryParse(jwtSettings["ExpirationDays"], out var days) ? days : 7;
        }

        public string GenerateToken(long userId, string username)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: DateTime.UtcNow.AddDays(_expirationDays),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public (bool isValid, long userId, string username) ValidateToken(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_secretKey);

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _issuer,
                    ValidAudience = _audience,
                    IssuerSigningKey = new SymmetricSecurityKey(key)
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
                
                var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
                var usernameClaim = principal.FindFirst(ClaimTypes.Name);

                if (userIdClaim != null && long.TryParse(userIdClaim.Value, out var userId) && usernameClaim != null)
                {
                    return (true, userId, usernameClaim.Value);
                }

                return (false, 0, string.Empty);
            }
            catch
            {
                return (false, 0, string.Empty);
            }
        }
    }
}
