namespace CryptoSpot.Core.Commands.Auth
{
    /// <summary>
    /// 登录命令 - Core层的纯业务对象（无验证注解）
    /// </summary>
    public class LoginCommand
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// 注册命令 - Core层的纯业务对象（无验证注解）
    /// </summary>
    public class RegisterCommand
    {
        public string Email { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// 认证结果 - Core层的纯数据契约
    /// </summary>
    public class AuthResult
    {
        public string Token { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}
