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

        // 内部: 通过仓储获取领域实体
        private async Task<User?> GetDomainUserByIdInternal(long userId)
        {
            return await _userRepository.GetByIdAsync(userId);
        }
    }
}
