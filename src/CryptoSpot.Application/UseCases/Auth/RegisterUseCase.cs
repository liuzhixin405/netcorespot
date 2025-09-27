using CryptoSpot.Application.Abstractions.Auth;
using CryptoSpot.Domain.Commands.Auth; // updated to new namespace
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
                if (string.IsNullOrWhiteSpace(request.Email?.Trim()) ||
                    string.IsNullOrWhiteSpace(request.Username?.Trim()) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return null;
                }

                var command = new RegisterCommand
                {
                    Email = request.Email.Trim().ToLowerInvariant(),
                    Username = request.Username.Trim().ToLowerInvariant(),
                    Password = request.Password
                };

                var result = await _authService.RegisterAsync(command);
                if (result is null)
                {
                    return null;
                }

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
