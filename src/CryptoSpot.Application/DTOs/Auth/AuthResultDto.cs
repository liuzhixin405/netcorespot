using CryptoSpot.Application.DTOs.Users;

namespace CryptoSpot.Application.DTOs.Auth
{
    /// <summary>
    /// 认证结果DTO（替代暴露Domain层的 AuthResult）
    /// </summary>
    public class AuthResultDto
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public UserDto? User { get; set; }
    }
}
