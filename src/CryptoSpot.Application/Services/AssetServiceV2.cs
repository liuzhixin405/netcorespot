using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Users;

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 文件已重命名: 原 AssetServiceV2.cs 逻辑并入 AssetService (应用层 DTO 门面)。
    /// </summary>
    public class AssetService : IAssetService
    {
        private readonly IAssetDomainService _domain; // 原 _assetService
        private readonly IDtoMappingService _mappingService;
        private readonly ILogger<AssetService> _logger;

        public AssetService(
            IAssetDomainService domain,
            IDtoMappingService mappingService,
            ILogger<AssetService> logger)
        {
            _domain = domain;
            _mappingService = mappingService;
            _logger = logger;
        }

        // 用户资产查询
        public async Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(int userId)
        {
            try
            {
                var assets = await _domain.GetUserAssetsAsync(userId);
                var dtoList = _mappingService.MapToDto(assets);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user assets for user {UserId}", userId);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateError("获取用户资产失败");
            }
        }

        public async Task<ApiResponseDto<AssetDto?>> GetUserAssetAsync(int userId, string symbol)
        {
            try
            {
                var asset = await _domain.GetUserAssetAsync(userId, symbol);
                var dto = asset != null ? _mappingService.MapToDto(asset) : null;
                return ApiResponseDto<AssetDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting asset {Symbol} for user {UserId}", symbol, userId);
                return ApiResponseDto<AssetDto?>.CreateError("获取用户资产失败");
            }
        }

        public async Task<ApiResponseDto<AssetSummaryDto>> GetUserAssetSummaryAsync(int userId)
        {
            try
            {
                var assets = await _domain.GetUserAssetsAsync(userId);
                var assetList = assets.ToList();
                
                var summary = new AssetSummaryDto
                {
                    TotalValue = assetList.Sum(a => a.Total * 1m), // 需要根据价格计算，暂时使用1:1
                    AvailableValue = assetList.Sum(a => a.Available * 1m),
                    FrozenValue = assetList.Sum(a => a.Frozen * 1m),
                    AssetTypes = assetList.Count(a => a.Total > 0),
                    LastUpdated = DateTime.UtcNow
                };
                
                return ApiResponseDto<AssetSummaryDto>.CreateSuccess(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user asset summary for user {UserId}", userId);
                return ApiResponseDto<AssetSummaryDto>.CreateError("获取用户资产汇总失败");
            }
        }

        // 系统资产查询
        public Task<ApiResponseDto<IEnumerable<AssetDto>>> GetSystemAssetsAsync()
        {
            try
            {
                // 由于原始服务没有GetSystemAssetsAsync方法，我们返回空集合
                var emptyAssets = new List<AssetDto>();
                _logger.LogWarning("GetSystemAssetsAsync not implemented - returning empty collection");
                return Task.FromResult(ApiResponseDto<IEnumerable<AssetDto>>.CreateSuccess(emptyAssets, "系统资产功能暂未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system assets");
                return Task.FromResult(ApiResponseDto<IEnumerable<AssetDto>>.CreateError("获取系统资产失败"));
            }
        }

        public Task<ApiResponseDto<AssetDto?>> GetSystemAssetAsync(string symbol)
        {
            try
            {
                _logger.LogWarning("GetSystemAssetAsync not implemented for symbol {Symbol}", symbol);
                return Task.FromResult(ApiResponseDto<AssetDto?>.CreateSuccess(null, "系统资产功能暂未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system asset {Symbol}", symbol);
                return Task.FromResult(ApiResponseDto<AssetDto?>.CreateError("获取系统资产失败"));
            }
        }

        // 资产操作
        public async Task<ApiResponseDto<bool>> AddAssetAsync(int userId, AssetOperationRequestDto request)
        {
            try
            {
                var success = await _domain.AddAssetAsync(userId, request.Symbol, request.Amount);
                return ApiResponseDto<bool>.CreateSuccess(success, success ? "资产增加成功" : "资产增加失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding asset {Symbol} for user {UserId}", request.Symbol, userId);
                return ApiResponseDto<bool>.CreateError("资产增加失败");
            }
        }

        public async Task<ApiResponseDto<bool>> DeductAssetAsync(int userId, AssetOperationRequestDto request)
        {
            try
            {
                var success = await _domain.DeductAssetAsync(userId, request.Symbol, request.Amount);
                return ApiResponseDto<bool>.CreateSuccess(success, success ? "资产扣除成功" : "资产扣除失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deducting asset {Symbol} for user {UserId}", request.Symbol, userId);
                return ApiResponseDto<bool>.CreateError("资产扣除失败");
            }
        }

        public async Task<ApiResponseDto<bool>> FreezeAssetAsync(int userId, AssetOperationRequestDto request)
        {
            try
            {
                var success = await _domain.FreezeAssetAsync(userId, request.Symbol, request.Amount);
                return ApiResponseDto<bool>.CreateSuccess(success, success ? "资产冻结成功" : "资产冻结失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error freezing asset {Symbol} for user {UserId}", request.Symbol, userId);
                return ApiResponseDto<bool>.CreateError("资产冻结失败");
            }
        }

        public async Task<ApiResponseDto<bool>> UnfreezeAssetAsync(int userId, AssetOperationRequestDto request)
        {
            try
            {
                var success = await _domain.UnfreezeAssetAsync(userId, request.Symbol, request.Amount);
                return ApiResponseDto<bool>.CreateSuccess(success, success ? "资产解冻成功" : "资产解冻失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unfreezing asset {Symbol} for user {UserId}", request.Symbol, userId);
                return ApiResponseDto<bool>.CreateError("资产解冻失败");
            }
        }

        // 资产转移
        public async Task<ApiResponseDto<bool>> TransferAssetAsync(int fromUserId, AssetTransferRequestDto request)
        {
            try
            {
                // 由于原始服务没有TransferAssetAsync，我们需要手动实现转账逻辑
                var fromAsset = await _domain.GetUserAssetAsync(fromUserId, request.Symbol);
                if (fromAsset == null || !await _domain.HasSufficientBalanceAsync(fromUserId, request.Symbol, request.Amount))
                {
                    return ApiResponseDto<bool>.CreateSuccess(false, "余额不足");
                }

                var deductSuccess = await _domain.DeductAssetAsync(fromUserId, request.Symbol, request.Amount);
                if (deductSuccess)
                {
                    var addSuccess = await _domain.AddAssetAsync(request.ToUserId, request.Symbol, request.Amount);
                    if (!addSuccess)
                    {
                        // 回滚扣除操作
                        await _domain.AddAssetAsync(fromUserId, request.Symbol, request.Amount);
                        return ApiResponseDto<bool>.CreateSuccess(false, "转账失败，已回滚");
                    }
                }

                return ApiResponseDto<bool>.CreateSuccess(deductSuccess, deductSuccess ? "转账成功" : "转账失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transferring asset from user {FromUserId} to user {ToUserId}", fromUserId, request.ToUserId);
                return ApiResponseDto<bool>.CreateError("转账失败");
            }
        }

        // 系统资产管理
        public Task<ApiResponseDto<bool>> RefillSystemAssetAsync(string symbol, decimal amount)
        {
            try
            {
                _logger.LogWarning("RefillSystemAssetAsync not implemented for symbol {Symbol}", symbol);
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "系统资产补充功能暂未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refilling system asset {Symbol}", symbol);
                return Task.FromResult(ApiResponseDto<bool>.CreateError("系统资产补充失败"));
            }
        }

        public Task<ApiResponseDto<bool>> UpdateSystemAssetConfigAsync(string symbol, decimal minReserve, decimal targetBalance, bool autoRefillEnabled)
        {
            try
            {
                _logger.LogWarning("UpdateSystemAssetConfigAsync not implemented for symbol {Symbol}", symbol);
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "系统资产配置功能暂未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating system asset config {Symbol}", symbol);
                return Task.FromResult(ApiResponseDto<bool>.CreateError("系统资产配置失败"));
            }
        }

        // 资产统计
        public async Task<ApiResponseDto<decimal>> GetTotalAssetValueAsync(int userId, string quoteCurrency = "USDT")
        {
            try
            {
                var assets = await _domain.GetUserAssetsAsync(userId);
                // 简化计算，假设所有资产与USDT是1:1比例
                var totalValue = assets.Sum(a => a.Total);
                return ApiResponseDto<decimal>.CreateSuccess(totalValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total asset value for user {UserId}", userId);
                return ApiResponseDto<decimal>.CreateError("获取资产总价值失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<AssetDto>>> GetAssetsAboveThresholdAsync(int userId, decimal threshold)
        {
            try
            {
                var assets = await _domain.GetUserAssetsAsync(userId);
                var filteredAssets = assets.Where(a => a.Total >= threshold);
                var dtoList = _mappingService.MapToDto(filteredAssets);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assets above threshold for user {UserId}", userId);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateError("获取资产失败");
            }
        }

        // 资产验证
        public async Task<ApiResponseDto<bool>> ValidateAssetOperationAsync(int userId, string symbol, decimal amount, bool includeFrozen = false)
        {
            try
            {
                var isValid = await _domain.HasSufficientBalanceAsync(userId, symbol, amount, includeFrozen);
                return ApiResponseDto<bool>.CreateSuccess(isValid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating asset operation for user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("资产操作验证失败");
            }
        }

        public async Task<ApiResponseDto<bool>> CheckAssetExistsAsync(int userId, string symbol)
        {
            try
            {
                var asset = await _domain.GetUserAssetAsync(userId, symbol);
                var exists = asset != null;
                return ApiResponseDto<bool>.CreateSuccess(exists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking asset exists for user {UserId} symbol {Symbol}", userId, symbol);
                return ApiResponseDto<bool>.CreateError("检查资产失败");
            }
        }
    }
}
