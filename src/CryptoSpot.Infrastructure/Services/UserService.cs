using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.Abstractions.Repositories;

namespace CryptoSpot.Infrastructure.Services
{
    /// 用户应用服务
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IDtoMappingService _mappingService;
        private readonly ILogger<UserService> _logger;
        private readonly IUnitOfWork _unitOfWork;
        private readonly RedisCacheService _cacheService;

        public UserService(
            IUserRepository userRepository,
            IDtoMappingService mappingService,
            ILogger<UserService> logger,
            IUnitOfWork unitOfWork,
            RedisCacheService cacheService)
        {
            _userRepository = userRepository;
            _mappingService = mappingService;
            _logger = logger;
            _unitOfWork = unitOfWork;
            _cacheService = cacheService;
        }

        // 基础用户操作 (内部先获取领域实体再转 DTO)
        public async Task<ApiResponseDto<UserDto?>> GetUserByIdAsync(long userId)
        {
            try
            {
                Domain.Entities.User? domainUser = null;
                if (_cacheService != null)
                {
                    try
                    {
                        domainUser = await _cacheService.GetUserAsync(userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Cache read failed for user {UserId}, fallback to DB", userId);
                    }
                }

                if (domainUser == null)
                {
                    domainUser = await GetDomainUserByIdInternal(userId);
                    if (domainUser != null && _cacheService != null)
                    {
                        await _cacheService.SetUserAsync(domainUser);
                    }
                }
                var dto = domainUser != null ? _mappingService.MapToDto(domainUser) : null;
                return ApiResponseDto<UserDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户失败 {UserId}", userId);
                return ApiResponseDto<UserDto?>.CreateError("获取用户信息失败");
            }
        }

        public async Task<ApiResponseDto<UserDto?>> GetUserByUsernameAsync(string username)
        {
            try
            {
                var user = (await _userRepository.FindAsync(u => u.Username == username)).FirstOrDefault();
                var dto = user != null ? _mappingService.MapToDto(user) : null;
                return ApiResponseDto<UserDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按用户名获取用户失败 {Username}", username);
                return ApiResponseDto<UserDto?>.CreateError("获取用户信息失败");
            }
        }

        public async Task<ApiResponseDto<UserDto?>> GetUserByEmailAsync(string email)
        {
            try
            {
                var user = (await _userRepository.FindAsync(u => u.Email == email)).FirstOrDefault();
                var dto = user != null ? _mappingService.MapToDto(user) : null;
                return ApiResponseDto<UserDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按邮箱获取用户失败 {Email}", email);
                return ApiResponseDto<UserDto?>.CreateError("获取用户信息失败");
            }
        }

        // 用户列表查询
        public async Task<ApiResponseDto<PagedResponseDto<UserDto>>> GetUsersAsync(PagedRequestDto request)
        {
            try
            {
                var all = await _userRepository.GetAllAsync();
                var total = all.Count();
                var pageItems = all.Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize);
                var dtoList = _mappingService.MapToDto(pageItems);
                var paged = new PagedResponseDto<UserDto>
                {
                    Items = dtoList,
                    TotalCount = total,
                    PageNumber = request.PageNumber,
                    PageSize = request.PageSize
                };
                return ApiResponseDto<PagedResponseDto<UserDto>>.CreateSuccess(paged);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分页用户失败");
                return ApiResponseDto<PagedResponseDto<UserDto>>.CreateError("获取用户列表失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<UserDto>>> GetSystemUsersAsync()
        {
            try
            {
                var users = await _userRepository.FindAsync(u => u.Type != UserType.Regular);
                var dtoList = _mappingService.MapToDto(users);
                return ApiResponseDto<IEnumerable<UserDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取系统用户失败");
                return ApiResponseDto<IEnumerable<UserDto>>.CreateError("获取系统用户失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<UserDto>>> GetActiveSystemUsersAsync()
        {
            try
            {
                var users = await _userRepository.FindAsync(u => u.Type != UserType.Regular && u.IsActive);
                var dtoList = _mappingService.MapToDto(users);
                return ApiResponseDto<IEnumerable<UserDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃系统用户失败");
                return ApiResponseDto<IEnumerable<UserDto>>.CreateError("获取活跃系统用户失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<UserSummaryDto>>> GetUserSummariesAsync()
        {
            try
            {
                var users = await _userRepository.FindAsync(u => u.Type != UserType.Regular);
                var summaries = users.Select(u => _mappingService.MapToSummaryDto(u));
                return ApiResponseDto<IEnumerable<UserSummaryDto>>.CreateSuccess(summaries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户摘要失败");
                return ApiResponseDto<IEnumerable<UserSummaryDto>>.CreateError("获取用户摘要失败");
            }
        }

        // 用户管理
        public async Task<ApiResponseDto<UserDto>> CreateUserAsync(CreateUserRequestDto request)
        {
            try
            {
                var user = _mappingService.MapToDomain(request);
                user.Touch();
                var created = await _userRepository.AddAsync(user);
                await _unitOfWork.SaveChangesAsync();

                // 缓存并标记脏
                try
                {
                    if (_cacheService != null)
                    {
                        await _cacheService.SetUserAsync(created);
                        await _cacheService.MarkUserDirtyAsync(created.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set cache for created user {UserId}", created.Id);
                }

                return ApiResponseDto<UserDto>.CreateSuccess(_mappingService.MapToDto(created), "用户创建成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建用户失败");
                return ApiResponseDto<UserDto>.CreateError("用户创建失败");
            }
        }

        public async Task<ApiResponseDto<UserDto>> UpdateUserAsync(long userId, UpdateUserRequestDto request)
        {
            try
            {
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null) return ApiResponseDto<UserDto>.CreateError("用户不存在");
                user.Email = request.Email ?? user.Email;
                user.Description = request.Description ?? user.Description;
                user.Touch();
                await _userRepository.UpdateAsync(user);

                // 写缓存并标脏
                try
                {
                    if (_cacheService != null)
                    {
                        await _cacheService.SetUserAsync(user);
                        await _cacheService.MarkUserDirtyAsync(user.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update cache for user {UserId}", user.Id);
                }

                return ApiResponseDto<UserDto>.CreateSuccess(_mappingService.MapToDto(user), "用户更新成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新用户失败 {UserId}", userId);
                return ApiResponseDto<UserDto>.CreateError("用户更新失败");
            }
        }

        public async Task<ApiResponseDto<bool>> DeleteUserAsync(long userId)
        {
            try
            {
                var user = await _userRepository.GetByIdAsync(userId);
                if (user != null)
                {
                    await _userRepository.DeleteAsync(user);
                    // 移除缓存
                    try
                    {
                        if (_cacheService != null)
                        {
                            await _cacheService.RemoveUserAsync(userId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove cache for deleted user {UserId}", userId);
                    }
                }
                return ApiResponseDto<bool>.CreateSuccess(true, "用户删除成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除用户失败 {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("用户删除失败");
            }
        }

        // 用户验证
        public async Task<ApiResponseDto<bool>> ValidateUserAsync(long userId)
        {
            try
            {
                var user = await _userRepository.GetByIdAsync(userId);
                var valid = user != null && user.IsActive;
                return ApiResponseDto<bool>.CreateSuccess(valid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证用户失败 {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("用户验证失败");
            }
        }

        public async Task<ApiResponseDto<bool>> CheckUsernameExistsAsync(string username)
        {
            try
            {
                var user = (await _userRepository.FindAsync(u => u.Username == username)).FirstOrDefault();
                return ApiResponseDto<bool>.CreateSuccess(user != null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查用户名失败 {Username}", username);
                return ApiResponseDto<bool>.CreateError("检查用户名失败");
            }
        }

        public async Task<ApiResponseDto<bool>> CheckEmailExistsAsync(string email)
        {
            try
            {
                var user = (await _userRepository.FindAsync(u => u.Email == email)).FirstOrDefault();
                return ApiResponseDto<bool>.CreateSuccess(user != null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查邮箱失败 {Email}", email);
                return ApiResponseDto<bool>.CreateError("检查邮箱失败");
            }
        }

        // 系统账号特殊操作 (暂未实现)
        public Task<ApiResponseDto<bool>> EnableAutoTradingAsync(long systemUserId, bool enabled)
            => Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "此功能尚未实现"));

        public Task<ApiResponseDto<bool>> UpdateTradingLimitsAsync(long systemUserId, decimal dailyLimit, decimal maxRiskRatio)
            => Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "此功能尚未实现"));

        public Task<ApiResponseDto<bool>> ResetDailyTradedAmountAsync(long systemUserId)
            => Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "此功能尚未实现"));

        // 内部: 通过仓储获取领域实体
        private async Task<User?> GetDomainUserByIdInternal(long userId)
        {
            return await _userRepository.GetByIdAsync(userId);
        }
    }
}
