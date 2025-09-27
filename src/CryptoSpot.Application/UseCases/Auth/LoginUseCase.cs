// Deprecated: 保留文件以防引用，推荐直接使用 IAuthService.LoginAsync(LoginRequest)
using CryptoSpot.Application.DTOs.Auth;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Abstractions.Services.Auth;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.UseCases.Auth
{
    public class LoginUseCase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<LoginUseCase> _logger;
        public LoginUseCase(IAuthService authService, ILogger<LoginUseCase> logger)
        {
            _authService = authService;
            _logger = logger;
        }
        public Task<ApiResponseDto<AuthResultDto?>> ExecuteAsync(LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                _logger.LogWarning("用户名为空");
                return Task.FromResult(ApiResponseDto<AuthResultDto?>.CreateError("用户名不能为空"));
            }
            return _authService.LoginAsync(request);
        }
    }
}
