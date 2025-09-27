using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Users;

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// DTO用户服务实现
    /// </summary>
    public class UserServiceV2 : IUserServiceV2
    {
        private readonly IUserService _userService; // 修复: 依赖领域用户服务，而不是自身接口
        private readonly IDtoMappingService _mappingService;
        private readonly ILogger<UserServiceV2> _logger;

        public UserServiceV2(
            IUserService userService,
            IDtoMappingService mappingService,
            ILogger<UserServiceV2> logger)
        {
            _userService = userService;
            _mappingService = mappingService;
            _logger = logger;
        }

        // 基础用户操作
        public async Task<ApiResponseDto<UserDto?>> GetUserByIdAsync(int userId)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(userId);
                var dto = user != null ? _mappingService.MapToDto(user) : null;
                return ApiResponseDto<UserDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user {UserId}", userId);
                return ApiResponseDto<UserDto?>.CreateError("获取用户信息失败");
            }
        }

        public async Task<ApiResponseDto<UserDto?>> GetUserByUsernameAsync(string username)
        {
            try
            {
                var user = await _userService.GetUserByUsernameAsync(username);
                var dto = user != null ? _mappingService.MapToDto(user) : null;
                return ApiResponseDto<UserDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by username {Username}", username);
                return ApiResponseDto<UserDto?>.CreateError("获取用户信息失败");
            }
        }

        public async Task<ApiResponseDto<UserDto?>> GetUserByEmailAsync(string email)
        {
            try
            {
                var user = await _userService.GetUserByEmailAsync(email);
                var dto = user != null ? _mappingService.MapToDto(user) : null;
                return ApiResponseDto<UserDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user by email {Email}", email);
                return ApiResponseDto<UserDto?>.CreateError("获取用户信息失败");
            }
        }        // 用户列表查询
        public async Task<ApiResponseDto<PagedResponseDto<UserDto>>> GetUsersAsync(PagedRequestDto request)
        {
            try
            {
                // 由于原始服务没有分页，我们自己实现简单分页
                var allUsers = await _userService.GetSystemUsersAsync();
                var totalCount = allUsers.Count();
                
                var pagedUsers = allUsers
                    .Skip((request.PageNumber - 1) * request.PageSize)
                    .Take(request.PageSize);
                
                var dtoList = _mappingService.MapToDto(pagedUsers);
                
                var pagedResponse = new PagedResponseDto<UserDto>
                {
                    Items = dtoList,
                    TotalCount = totalCount,
                    PageNumber = request.PageNumber,
                    PageSize = request.PageSize
                };
                
                return ApiResponseDto<PagedResponseDto<UserDto>>.CreateSuccess(pagedResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting paged users");
                return ApiResponseDto<PagedResponseDto<UserDto>>.CreateError("获取用户列表失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<UserDto>>> GetSystemUsersAsync()
        {
            try
            {
                var users = await _userService.GetSystemUsersAsync();
                var dtoList = _mappingService.MapToDto(users);
                return ApiResponseDto<IEnumerable<UserDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system users");
                return ApiResponseDto<IEnumerable<UserDto>>.CreateError("获取系统用户失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<UserDto>>> GetActiveSystemUsersAsync()
        {
            try
            {
                var users = await _userService.GetActiveSystemUsersAsync();
                var dtoList = _mappingService.MapToDto(users);
                return ApiResponseDto<IEnumerable<UserDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active system users");
                return ApiResponseDto<IEnumerable<UserDto>>.CreateError("获取活跃系统用户失败");
            }
        }        public async Task<ApiResponseDto<IEnumerable<UserSummaryDto>>> GetUserSummariesAsync()
        {
            try
            {
                var users = await _userService.GetSystemUsersAsync();
                var summaries = users.Select(u => _mappingService.MapToSummaryDto(u));
                return ApiResponseDto<IEnumerable<UserSummaryDto>>.CreateSuccess(summaries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user summaries");
                return ApiResponseDto<IEnumerable<UserSummaryDto>>.CreateError("获取用户摘要失败");
            }
        }

        // 用户管理
        public async Task<ApiResponseDto<UserDto>> CreateUserAsync(CreateUserRequestDto request)
        {
            try
            {
                var user = _mappingService.MapToDomain(request);
                var createdUser = await _userService.CreateUserAsync(user);
                
                var dto = _mappingService.MapToDto(createdUser);
                return ApiResponseDto<UserDto>.CreateSuccess(dto, "用户创建成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                return ApiResponseDto<UserDto>.CreateError("用户创建失败");
            }
        }

        public async Task<ApiResponseDto<UserDto>> UpdateUserAsync(int userId, UpdateUserRequestDto request)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return ApiResponseDto<UserDto>.CreateError("用户不存在");
                }

                // 更新用户属性
                user.Email = request.Email ?? user.Email;
                user.Description = request.Description ?? user.Description;

                await _userService.UpdateUserAsync(user);
                
                var dto = _mappingService.MapToDto(user);
                return ApiResponseDto<UserDto>.CreateSuccess(dto, "用户更新成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user {UserId}", userId);
                return ApiResponseDto<UserDto>.CreateError("用户更新失败");
            }
        }

        public async Task<ApiResponseDto<bool>> DeleteUserAsync(int userId)
        {
            try
            {
                await _userService.DeleteUserAsync(userId);
                return ApiResponseDto<bool>.CreateSuccess(true, "用户删除成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("用户删除失败");
            }
        }

        // 用户验证 - 这些方法在原始服务中不存在，需要基于现有方法实现
        public async Task<ApiResponseDto<bool>> ValidateUserAsync(int userId)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(userId);
                var isValid = user != null && user.IsActive;
                return ApiResponseDto<bool>.CreateSuccess(isValid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("用户验证失败");
            }
        }

        public async Task<ApiResponseDto<bool>> CheckUsernameExistsAsync(string username)
        {
            try
            {
                var user = await _userService.GetUserByUsernameAsync(username);
                var exists = user != null;
                return ApiResponseDto<bool>.CreateSuccess(exists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking username exists {Username}", username);
                return ApiResponseDto<bool>.CreateError("检查用户名失败");
            }
        }

        public async Task<ApiResponseDto<bool>> CheckEmailExistsAsync(string email)
        {
            try
            {
                var user = await _userService.GetUserByEmailAsync(email);
                var exists = user != null;
                return ApiResponseDto<bool>.CreateSuccess(exists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email exists {Email}", email);
                return ApiResponseDto<bool>.CreateError("检查邮箱失败");
            }
        }        // 系统账号特殊操作 - 这些功能在原始服务中不存在，返回未实现的响应
        public Task<ApiResponseDto<bool>> EnableAutoTradingAsync(int systemUserId, bool enabled)
        {
            try
            {
                _logger.LogWarning("EnableAutoTradingAsync not implemented for user {UserId}", systemUserId);
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "此功能尚未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling auto trading for user {UserId}", systemUserId);
                return Task.FromResult(ApiResponseDto<bool>.CreateError("启用自动交易失败"));
            }
        }

        public Task<ApiResponseDto<bool>> UpdateTradingLimitsAsync(int systemUserId, decimal dailyLimit, decimal maxRiskRatio)
        {
            try
            {
                _logger.LogWarning("UpdateTradingLimitsAsync not implemented for user {UserId}", systemUserId);
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "此功能尚未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating trading limits for user {UserId}", systemUserId);
                return Task.FromResult(ApiResponseDto<bool>.CreateError("更新交易限制失败"));
            }
        }

        public Task<ApiResponseDto<bool>> ResetDailyTradedAmountAsync(int systemUserId)
        {
            try
            {
                _logger.LogWarning("ResetDailyTradedAmountAsync not implemented for user {UserId}", systemUserId);
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "此功能尚未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting daily traded amount for user {UserId}", systemUserId);
                return Task.FromResult(ApiResponseDto<bool>.CreateError("重置日交易量失败"));
            }
        }
    }
}
