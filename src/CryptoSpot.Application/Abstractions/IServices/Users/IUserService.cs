using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Application.Abstractions.Services.Users
{
    /// 用户服务接口
    public interface IUserService
    {
        // 基础用户操作
        Task<ApiResponseDto<UserDto?>> GetUserByIdAsync(long userId);
    }
}
