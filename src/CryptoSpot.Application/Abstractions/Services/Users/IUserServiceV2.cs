using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Application.Abstractions.Services.Users
{
    /// <summary>
    /// 用户服务接口 - 使用DTO
    /// </summary>
    public interface IUserServiceV2
    {
        // 基础用户操作
        Task<ApiResponseDto<UserDto?>> GetUserByIdAsync(int userId);
        Task<ApiResponseDto<UserDto?>> GetUserByUsernameAsync(string username);
        Task<ApiResponseDto<UserDto?>> GetUserByEmailAsync(string email);
        
        // 用户列表查询
        Task<ApiResponseDto<PagedResponseDto<UserDto>>> GetUsersAsync(PagedRequestDto request);
        Task<ApiResponseDto<IEnumerable<UserDto>>> GetSystemUsersAsync();
        Task<ApiResponseDto<IEnumerable<UserDto>>> GetActiveSystemUsersAsync();
        Task<ApiResponseDto<IEnumerable<UserSummaryDto>>> GetUserSummariesAsync();
        
        // 用户管理
        Task<ApiResponseDto<UserDto>> CreateUserAsync(CreateUserRequestDto request);
        Task<ApiResponseDto<UserDto>> UpdateUserAsync(int userId, UpdateUserRequestDto request);
        Task<ApiResponseDto<bool>> DeleteUserAsync(int userId);
        
        // 用户验证
        Task<ApiResponseDto<bool>> ValidateUserAsync(int userId);
        Task<ApiResponseDto<bool>> CheckUsernameExistsAsync(string username);
        Task<ApiResponseDto<bool>> CheckEmailExistsAsync(string email);
        
        // 系统账号特殊操作
        Task<ApiResponseDto<bool>> EnableAutoTradingAsync(int systemUserId, bool enabled);
        Task<ApiResponseDto<bool>> UpdateTradingLimitsAsync(int systemUserId, decimal dailyLimit, decimal maxRiskRatio);
        Task<ApiResponseDto<bool>> ResetDailyTradedAmountAsync(int systemUserId);
    }
}
