// Deprecated: 保留文件以防引用，推荐直接使用 IAuthService.RegisterAsync(RegisterRequest)
using CryptoSpot.Application.DTOs.Auth;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Abstractions.Services.Auth;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.UseCases.Auth
{
    public class RegisterUseCase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<RegisterUseCase> _logger;
        public RegisterUseCase(IAuthService authService, ILogger<RegisterUseCase> logger)
        {
            _authService = authService;
            _logger = logger;
        }
        public Task<ApiResponseDto<AuthResultDto?>> ExecuteAsync(RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email))
            {
                _logger.LogWarning("注册信息不完整");
                return Task.FromResult(ApiResponseDto<AuthResultDto?>.CreateError("注册信息不完整"));
            }
            return _authService.RegisterAsync(request);
        }
    }
}
