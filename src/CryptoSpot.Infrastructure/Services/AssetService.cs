using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Domain.Entities; // 新增
using CryptoSpot.Application.Abstractions.Repositories; // 新增仓储

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 资产服务：同时提供 DTO 和 Raw(领域实体) 方法，整合原 AssetDomainService。
    /// </summary>
    public class AssetService : IAssetService
    {
        private readonly IAssetRepository _assetRepository; // 新增: 直接仓储
        private readonly IUnitOfWork _unitOfWork; // 新增: 事务支持
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

        // ================= DTO 层实现（改为调用 Raw 方法） =================
        public async Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(int userId)
        {
            try
            {
                var assets = await GetUserAssetsRawAsync(userId);
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
                var asset = await GetUserAssetRawAsync(userId, symbol);
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
                var assets = (await GetUserAssetsRawAsync(userId)).ToList();
                var summary = new AssetSummaryDto
                {
                    TotalValue = assets.Sum(a => a.Total),
                    AvailableValue = assets.Sum(a => a.Available),
                    FrozenValue = assets.Sum(a => a.Frozen),
                    AssetTypes = assets.Count(a => a.Total > 0),
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

        public Task<ApiResponseDto<IEnumerable<AssetDto>>> GetSystemAssetsAsync()
        {
            try
            {
                return Task.FromResult(ApiResponseDto<IEnumerable<AssetDto>>.CreateSuccess(Enumerable.Empty<AssetDto>(), "系统资产功能暂未实现"));
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
                return Task.FromResult(ApiResponseDto<AssetDto?>.CreateSuccess(null, "系统资产功能暂未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system asset {Symbol}", symbol);
                return Task.FromResult(ApiResponseDto<AssetDto?>.CreateError("获取系统资产失败"));
            }
        }

        public async Task<ApiResponseDto<bool>> AddAssetAsync(int userId, AssetOperationRequestDto request)
        {
            try
            {
                var success = await AddAssetRawAsync(userId, request.Symbol, request.Amount);
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
                var success = await DeductAssetRawAsync(userId, request.Symbol, request.Amount);
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
                var success = await FreezeAssetRawAsync(userId, request.Symbol, request.Amount);
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
                var success = await UnfreezeAssetRawAsync(userId, request.Symbol, request.Amount);
                return ApiResponseDto<bool>.CreateSuccess(success, success ? "资产解冻成功" : "资产解冻失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unfreezing asset {Symbol} for user {UserId}", request.Symbol, userId);
                return ApiResponseDto<bool>.CreateError("资产解冻失败");
            }
        }

        public async Task<ApiResponseDto<bool>> TransferAssetAsync(int fromUserId, AssetTransferRequestDto request)
        {
            try
            {
                var fromAsset = await GetUserAssetRawAsync(fromUserId, request.Symbol);
                if (fromAsset == null || !await HasSufficientBalanceRawAsync(fromUserId, request.Symbol, request.Amount))
                {
                    return ApiResponseDto<bool>.CreateSuccess(false, "余额不足");
                }
                var deductSuccess = await DeductAssetRawAsync(fromUserId, request.Symbol, request.Amount);
                if (deductSuccess)
                {
                    var addSuccess = await AddAssetRawAsync(request.ToUserId, request.Symbol, request.Amount);
                    if (!addSuccess)
                    {
                        await AddAssetRawAsync(fromUserId, request.Symbol, request.Amount); // 回滚
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

        public Task<ApiResponseDto<bool>> RefillSystemAssetAsync(string symbol, decimal amount)
        {
            try
            {
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
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "系统资产配置功能暂未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating system asset config {Symbol}", symbol);
                return Task.FromResult(ApiResponseDto<bool>.CreateError("系统资产配置失败"));
            }
        }

        public async Task<ApiResponseDto<decimal>> GetTotalAssetValueAsync(int userId, string quoteCurrency = "USDT")
        {
            try
            {
                var assets = await GetUserAssetsRawAsync(userId);
                var totalValue = assets.Sum(a => a.Total); // 简化
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
                var assets = await GetUserAssetsRawAsync(userId);
                var filtered = assets.Where(a => a.Total >= threshold);
                var dtoList = _mappingService.MapToDto(filtered);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assets above threshold for user {UserId}", userId);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateError("获取资产失败");
            }
        }

        public async Task<ApiResponseDto<bool>> ValidateAssetOperationAsync(int userId, string symbol, decimal amount, bool includeFrozen = false)
        {
            try
            {
                var isValid = await HasSufficientBalanceRawAsync(userId, symbol, amount, includeFrozen);
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
                var asset = await GetUserAssetRawAsync(userId, symbol);
                return ApiResponseDto<bool>.CreateSuccess(asset != null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking asset exists for user {UserId} symbol {Symbol}", userId, symbol);
                return ApiResponseDto<bool>.CreateError("检查资产失败");
            }
        }

        // ================= Raw 实体方法实现 =================
        public Task<IEnumerable<Asset>> GetUserAssetsRawAsync(int userId) => _assetRepository.FindAsync(a => a.UserId == userId);

        public async Task<Asset?> GetUserAssetRawAsync(int userId, string symbol)
        {
            var assets = await _assetRepository.FindAsync(a => a.UserId == userId && a.Symbol == symbol);
            return assets.FirstOrDefault();
        }

        public async Task<Asset> CreateUserAssetRawAsync(int userId, string symbol, decimal available = 0, decimal frozen = 0)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var asset = new Asset
            {
                UserId = userId,
                Symbol = symbol,
                Available = available,
                Frozen = frozen,
                CreatedAt = now,
                UpdatedAt = now
            };
            return await _assetRepository.AddAsync(asset);
        }

        public async Task<Asset> UpdateAssetBalanceRawAsync(int userId, string symbol, decimal available, decimal frozen)
        {
            var asset = await GetOrCreateAsync(userId, symbol);
            asset.Available = available;
            asset.Frozen = frozen;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return asset;
        }

        public async Task<bool> HasSufficientBalanceRawAsync(int userId, string symbol, decimal amount, bool includeFrozen = false)
        {
            var asset = await GetUserAssetRawAsync(userId, symbol);
            if (asset == null) return false;
            var balance = includeFrozen ? asset.Total : asset.Available;
            return balance >= amount;
        }

        public async Task<bool> FreezeAssetRawAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetOrCreateAsync(userId, symbol);
            if (asset.Available < amount) return false;
            asset.Available -= amount;
            asset.Frozen += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return true;
        }

        public async Task<bool> UnfreezeAssetRawAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetOrCreateAsync(userId, symbol);
            if (asset.Frozen < amount) return false;
            asset.Frozen -= amount;
            asset.Available += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return true;
        }

        public async Task<bool> DeductAssetRawAsync(int userId, string symbol, decimal amount, bool fromFrozen = false)
        {
            var asset = await GetOrCreateAsync(userId, symbol);
            if (fromFrozen)
            {
                if (asset.Frozen < amount) return false;
                asset.Frozen -= amount;
            }
            else
            {
                if (asset.Available < amount) return false;
                asset.Available -= amount;
            }
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return true;
        }

        public async Task<bool> AddAssetRawAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetOrCreateAsync(userId, symbol);
            asset.Available += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return true;
        }

        public async Task InitializeUserAssetsRawAsync(int userId, Dictionary<string, decimal> initialBalances)
        {
            foreach (var kv in initialBalances)
            {
                await AddAssetRawAsync(userId, kv.Key, kv.Value);
            }
        }

        private async Task<Asset> GetOrCreateAsync(int userId, string symbol)
        {
            var existing = await GetUserAssetRawAsync(userId, symbol);
            if (existing != null) return existing;
            return await CreateUserAssetRawAsync(userId, symbol);
        }
    }
}
