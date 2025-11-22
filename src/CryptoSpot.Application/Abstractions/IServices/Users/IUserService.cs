using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Application.Abstractions.Services.Users
{
    /// 用户服务接口
    public interface IUserService
    {
        // 基础用户操作
        Task<ApiResponseDto<UserDto?>> GetUserByIdAsync(long userId);
        Task<ApiResponseDto<UserDto?>> GetUserByUsernameAsync(string username);
        Task<ApiResponseDto<UserDto?>> GetUserByEmailAsync(string email);
        
        // 用户列表查询
        Task<ApiResponseDto<PagedResponseDto<UserDto>>> GetUsersAsync(PagedRequestDto request);
        Task<ApiResponseDto<IEnumerable<UserDto>>> GetSystemUsersAsync();
        Task<ApiResponseDto<IEnumerable<UserDto>>> GetActiveSystemUsersAsync();
        Task<ApiResponseDto<IEnumerable<UserSummaryDto>>> GetUserSummariesAsync();
        
        // 用户管理
        Task<ApiResponseDto<UserDto>> CreateUserAsync(CreateUserRequestDto request);
        Task<ApiResponseDto<UserDto>> UpdateUserAsync(long userId, UpdateUserRequestDto request);
        Task<ApiResponseDto<bool>> DeleteUserAsync(long userId);
        
        // 用户验证
        Task<ApiResponseDto<bool>> ValidateUserAsync(long userId);
        Task<ApiResponseDto<bool>> CheckUsernameExistsAsync(string username);
        Task<ApiResponseDto<bool>> CheckEmailExistsAsync(string email);
        
        // 系统账号特殊操作 (暂未实现)
        Task<ApiResponseDto<bool>> EnableAutoTradingAsync(long systemUserId, bool enabled);
        Task<ApiResponseDto<bool>> UpdateTradingLimitsAsync(long systemUserId, decimal dailyLimit, decimal maxRiskRatio);
        Task<ApiResponseDto<bool>> ResetDailyTradedAmountAsync(long systemUserId);
    }
}
