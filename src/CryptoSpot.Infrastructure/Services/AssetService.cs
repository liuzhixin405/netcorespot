using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 资产服务实现 - 纯数据库实现
    /// </summary>
    public class AssetService : IAssetService
    {
        private readonly IAssetRepository _assetRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDtoMappingService _mappingService;
        private readonly ILogger<AssetService> _logger;

        public AssetService(
            IAssetRepository assetRepository,
            IUnitOfWork unitOfWork,
            IDtoMappingService mappingService,
            ILogger<AssetService> logger)
        {
            _assetRepository = assetRepository;
            _unitOfWork = unitOfWork;
            _mappingService = mappingService;
            _logger = logger;
        }

        public async Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(long userId)
        {
            try
            {
                var assets = await _assetRepository.GetAssetsByUserIdAsync((int)userId);
                var dtos = assets.Select(_mappingService.MapToDto);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateSuccess(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assets for user {UserId}", userId);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateError("获取资产失败");
            }
        }

        public async Task<ApiResponseDto<AssetDto?>> GetUserAssetAsync(long userId, string symbol)
        {
            try
            {
                var asset = await _assetRepository.GetAssetByUserIdAndSymbolAsync((int)userId, symbol);
                if (asset == null)
                    return ApiResponseDto<AssetDto?>.CreateError("资产不存在");

                var dto = _mappingService.MapToDto(asset);
                return ApiResponseDto<AssetDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting asset {Symbol} for user {UserId}", symbol, userId);
                return ApiResponseDto<AssetDto?>.CreateError("获取资产失败");
            }
        }

        public async Task<ApiResponseDto<bool>> AddAssetAsync(long userId, AssetOperationRequestDto request)
        {
            try
            {
                var success = await _assetRepository.UpdateBalanceAsync((int)userId, request.Symbol, request.Amount);
                if (success)
                    await _unitOfWork.SaveChangesAsync();

                return success
                    ? ApiResponseDto<bool>.CreateSuccess(true, "资产增加成功")
                    : ApiResponseDto<bool>.CreateError("资产增加失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding asset for user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("资产增加失败");
            }
        }

        public async Task<ApiResponseDto<bool>> DeductAssetAsync(long userId, AssetOperationRequestDto request)
        {
            try
            {
                var success = await _assetRepository.UpdateBalanceAsync((int)userId, request.Symbol, -request.Amount);
                if (success)
                    await _unitOfWork.SaveChangesAsync();

                return success
                    ? ApiResponseDto<bool>.CreateSuccess(true, "资产扣减成功")
                    : ApiResponseDto<bool>.CreateError("资产扣减失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deducting asset for user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("资产扣减失败");
            }
        }

        public async Task<ApiResponseDto<bool>> FreezeAssetAsync(long userId, AssetOperationRequestDto request)
        {
            try
            {
                var success = await _assetRepository.FreezeAssetAsync((int)userId, request.Symbol, request.Amount);
                if (success)
                    await _unitOfWork.SaveChangesAsync();

                return success
                    ? ApiResponseDto<bool>.CreateSuccess(true, "资产冻结成功")
                    : ApiResponseDto<bool>.CreateError("资产冻结失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error freezing asset for user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("资产冻结失败");
            }
        }

        public async Task<ApiResponseDto<bool>> UnfreezeAssetAsync(long userId, AssetOperationRequestDto request)
        {
            try
            {
                var success = await _assetRepository.UnfreezeAssetAsync((int)userId, request.Symbol, request.Amount);
                if (success)
                    await _unitOfWork.SaveChangesAsync();

                return success
                    ? ApiResponseDto<bool>.CreateSuccess(true, "资产解冻成功")
                    : ApiResponseDto<bool>.CreateError("资产解冻失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unfreezing asset for user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("资产解冻失败");
            }
        }

        public async Task<ApiResponseDto<bool>> ConsumeFrozenAssetAsync(long userId, AssetOperationRequestDto request)
        {
            try
            {
                // Unfreeze then deduct
                var unfreezeSuccess = await _assetRepository.UnfreezeAssetAsync((int)userId, request.Symbol, request.Amount);
                if (!unfreezeSuccess)
                    return ApiResponseDto<bool>.CreateError("解冻资产失败");

                var deductSuccess = await _assetRepository.UpdateBalanceAsync((int)userId, request.Symbol, -request.Amount);
                if (!deductSuccess)
                    return ApiResponseDto<bool>.CreateError("扣减资产失败");

                await _unitOfWork.SaveChangesAsync();
                return ApiResponseDto<bool>.CreateSuccess(true, "消耗冻结资产成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consuming frozen asset for user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("消耗冻结资产失败");
            }
        }

        public async Task<ApiResponseDto<bool>> TransferAssetAsync(long fromUserId, AssetTransferRequestDto request)
        {
            try
            {
                // Deduct from sender
                var deductSuccess = await _assetRepository.UpdateBalanceAsync((int)fromUserId, request.Symbol, -request.Amount);
                if (!deductSuccess)
                    return ApiResponseDto<bool>.CreateError("扣减发送方资产失败");

                // Add to receiver
                var addSuccess = await _assetRepository.UpdateBalanceAsync((int)request.ToUserId, request.Symbol, request.Amount);
                if (!addSuccess)
                    return ApiResponseDto<bool>.CreateError("增加接收方资产失败");

                await _unitOfWork.SaveChangesAsync();
                return ApiResponseDto<bool>.CreateSuccess(true, "资产转账成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transferring asset from user {FromUserId} to {ToUserId}", fromUserId, request.ToUserId);
                return ApiResponseDto<bool>.CreateError("资产转账失败");
            }
        }

        public async Task<ApiResponseDto<bool>> InitializeUserAssetsAsync(long userId, Dictionary<string, decimal> initialBalances)
        {
            try
            {
                foreach (var (symbol, balance) in initialBalances)
                {
                    var asset = new Asset
                    {
                        UserId = (int)userId,
                        Symbol = symbol,
                        Available = balance,
                        Frozen = 0,
                        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    await _assetRepository.AddAsync(asset);
                }

                await _unitOfWork.SaveChangesAsync();
                return ApiResponseDto<bool>.CreateSuccess(true, "资产初始化成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing assets for user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("资产初始化失败");
            }
        }
    }
}
