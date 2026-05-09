using CryptoSpot.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CryptoSpot.Infrastructure.Identity
{
    public class JwtTokenService : ITokenService
    {
        private readonly JwtSettings _settings;
        private readonly TimeProvider _timeProvider;

        public JwtTokenService(IOptions<JwtSettings> options, TimeProvider timeProvider)
        {
            _settings = options.Value;
            _timeProvider = timeProvider;

            if (string.IsNullOrEmpty(_settings.SecretKey))
                throw new InvalidOperationException(
                    "JWT SecretKey is not configured. Set 'JwtSettings:SecretKey' in configuration.");
        }

        public string GenerateToken(long userId, string username)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires: _timeProvider.GetUtcNow().AddDays(_settings.ExpiryInDays).UtcDateTime,
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public (bool isValid, long userId, string username) ValidateToken(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_settings.SecretKey);

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _settings.Issuer,
                    ValidAudience = _settings.Audience,
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
