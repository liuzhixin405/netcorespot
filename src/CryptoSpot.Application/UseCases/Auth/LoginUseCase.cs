using CryptoSpot.Application.Abstractions.Auth; // replaced Core.Interfaces.Auth
using CryptoSpot.Domain.Commands.Auth; // replaced CryptoSpot.Core.Commands.Auth
using CryptoSpot.Application.DTOs.Auth;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.UseCases.Auth
{
    /// <summary>
    /// 登录用例 - 展示DTOs到Commands的转换价值
    /// </summary>
    public class LoginUseCase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<LoginUseCase> _logger;

        public LoginUseCase(IAuthService authService, ILogger<LoginUseCase> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        /// <summary>
        /// 执行登录用例
        /// DTOs → Commands → Core Service → Result → DTOs
        /// </summary>
        public async Task<AuthResponse?> ExecuteAsync(LoginRequest request)
        {
            try
            {
                // 1. 业务验证（Application层的职责）
                if (string.IsNullOrWhiteSpace(request.Username?.Trim()))
                {
                    _logger.LogWarning("Login attempt with empty username");
                    return null;
                }

                // 2. 转换 DTO → Command（这就是转换的价值！）
                var command = new LoginCommand
                {
                    Username = request.Username.Trim().ToLowerInvariant(), // 业务规则：用户名标准化
                    Password = request.Password
                };

                // 3. 调用Core层服务
                var result = await _authService.LoginAsync(command);
                if (result is null)
                {
                    return null;
                }

                // 4. 转换 Core Result → Application DTO
                return new AuthResponse
                {
                    Token = result.Token,
                    Username = result.Username,
                    Email = result.Email,
                    ExpiresAt = result.ExpiresAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in login use case");
                return null;
            }
        }
    }
}
