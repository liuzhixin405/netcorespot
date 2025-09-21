using CryptoSpot.Core.Interfaces.Auth;
using CryptoSpot.Core.Commands.Auth;
using CryptoSpot.Application.DTOs.Auth;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.UseCases.Auth
{
    /// <summary>
    /// 注册用例
    /// </summary>
    public class RegisterUseCase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<RegisterUseCase> _logger;

        public RegisterUseCase(IAuthService authService, ILogger<RegisterUseCase> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        public async Task<AuthResponse?> ExecuteAsync(RegisterRequest request)
        {
            try
            {
                // 业务验证
                if (string.IsNullOrWhiteSpace(request.Email?.Trim()) ||
                    string.IsNullOrWhiteSpace(request.Username?.Trim()) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return null;
                }

                // 转换 DTO → Command
                var command = new RegisterCommand
                {
                    Email = request.Email.Trim().ToLowerInvariant(),
                    Username = request.Username.Trim().ToLowerInvariant(),
                    Password = request.Password
                };

                // 调用Core层服务
                var result = await _authService.RegisterAsync(command);
                if (result == null)
                {
                    return null;
                }

                // 转换 Core Result → Application DTO
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
                _logger.LogError(ex, "Error in register use case for user: {Username}", request.Username);
                return null;
            }
        }
    }
}
