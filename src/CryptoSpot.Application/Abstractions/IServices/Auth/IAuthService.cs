using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Auth;
using CryptoSpot.Application.DTOs.Users;

namespace CryptoSpot.Application.Abstractions.Services.Auth
{
    public interface IAuthService
    {
        // 认证相关
        Task<ApiResponseDto<AuthResultDto?>> LoginAsync(LoginRequest request);
        Task<ApiResponseDto<AuthResultDto?>> RegisterAsync(RegisterRequest request);

        // 用户信息
        Task<ApiResponseDto<UserDto?>> GetCurrentUserAsync(int userId);

        // Token校验
        Task<ApiResponseDto<bool>> ValidateTokenAsync(string token);

        // 登出
        Task<ApiResponseDto<bool>> LogoutAsync(int userId);
    }
}
