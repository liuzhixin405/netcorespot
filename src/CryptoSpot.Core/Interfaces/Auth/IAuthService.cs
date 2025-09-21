using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Commands.Auth;

namespace CryptoSpot.Core.Interfaces.Auth
{
    /// <summary>
    /// 认证服务接口 - 使用Commands保持类型安全
    /// </summary>
    public interface IAuthService
    {
        /// <summary>
        /// 用户登录
        /// </summary>
        Task<AuthResult?> LoginAsync(LoginCommand command);
        
        /// <summary>
        /// 用户注册
        /// </summary>
        Task<AuthResult?> RegisterAsync(RegisterCommand command);
        
        /// <summary>
        /// 获取当前用户
        /// </summary>
        Task<User?> GetCurrentUserAsync(int userId);
        
        /// <summary>
        /// 验证Token
        /// </summary>
        Task<bool> ValidateTokenAsync(string token);
        
        /// <summary>
        /// 用户登出
        /// </summary>
        Task LogoutAsync(int userId);
    }
}
