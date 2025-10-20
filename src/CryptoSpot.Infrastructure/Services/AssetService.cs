using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Domain.Entities; // 内部仍可使用领域实体
using CryptoSpot.Application.Abstractions.Repositories;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 资产服务实现（仅对外暴露 DTO 接口；领域实体操作封装为私有方法）。
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

        #region DTO 接口实现
        public async Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(int userId)
        {
            try
            {
                var assets = await GetUserAssetsInternalAsync(userId);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateSuccess(_mappingService.MapToDto(assets));
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
                var asset = await GetUserAssetInternalAsync(userId, symbol);
                return ApiResponseDto<AssetDto?>.CreateSuccess(asset == null ? null : _mappingService.MapToDto(asset));
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
                var list = (await GetUserAssetsInternalAsync(userId)).ToList();
                var summary = new AssetSummaryDto
                {
                    TotalValue = list.Sum(a => a.Total),
                    AvailableValue = list.Sum(a => a.Available),
                    FrozenValue = list.Sum(a => a.Frozen),
                    AssetTypes = list.Count(a => a.Total > 0),
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
            => Task.FromResult(ApiResponseDto<IEnumerable<AssetDto>>.CreateSuccess(Enumerable.Empty<AssetDto>(), "系统资产功能暂未实现"));

        public Task<ApiResponseDto<AssetDto?>> GetSystemAssetAsync(string symbol)
            => Task.FromResult(ApiResponseDto<AssetDto?>.CreateSuccess(null, "系统资产功能暂未实现"));

        public async Task<ApiResponseDto<bool>> AddAssetAsync(int userId, AssetOperationRequestDto request)
        {
            try
            {
                var ok = await AddAssetInternalAsync(userId, request.Symbol, request.Amount);
                return ApiResponseDto<bool>.CreateSuccess(ok, ok ? "资产增加成功" : "资产增加失败");
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
                var ok = await DeductAssetInternalAsync(userId, request.Symbol, request.Amount);
                return ApiResponseDto<bool>.CreateSuccess(ok, ok ? "资产扣除成功" : "资产扣除失败");
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
                var ok = await FreezeAssetInternalAsync(userId, request.Symbol, request.Amount);
                return ApiResponseDto<bool>.CreateSuccess(ok, ok ? "资产冻结成功" : "资产冻结失败");
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
                var ok = await UnfreezeAssetInternalAsync(userId, request.Symbol, request.Amount);
                return ApiResponseDto<bool>.CreateSuccess(ok, ok ? "资产解冻成功" : "资产解冻失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unfreezing asset {Symbol} for user {UserId}", request.Symbol, userId);
                return ApiResponseDto<bool>.CreateError("资产解冻失败");
            }
        }

        public async Task<ApiResponseDto<bool>> ConsumeFrozenAssetAsync(int userId, AssetOperationRequestDto request)
        {
            try
            {
                var ok = await DeductAssetInternalAsync(userId, request.Symbol, request.Amount, fromFrozen: true);
                return ApiResponseDto<bool>.CreateSuccess(ok, ok ? "冻结资产消耗成功" : "冻结资产消耗失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consuming frozen asset {Symbol} for user {UserId}", request.Symbol, userId);
                return ApiResponseDto<bool>.CreateError("冻结资产消耗失败");
            }
        }

        public async Task<ApiResponseDto<bool>> TransferAssetAsync(int fromUserId, AssetTransferRequestDto request)
        {
            try
            {
                var fromAsset = await GetUserAssetInternalAsync(fromUserId, request.Symbol);
                if (fromAsset == null || !await HasSufficientBalanceInternalAsync(fromUserId, request.Symbol, request.Amount))
                {
                    return ApiResponseDto<bool>.CreateSuccess(false, "余额不足");
                }
                var deduct = await DeductAssetInternalAsync(fromUserId, request.Symbol, request.Amount);
                if (deduct)
                {
                    var add = await AddAssetInternalAsync(request.ToUserId, request.Symbol, request.Amount);
                    if (!add)
                    {
                        await AddAssetInternalAsync(fromUserId, request.Symbol, request.Amount); // 回滚
                        return ApiResponseDto<bool>.CreateSuccess(false, "转账失败，已回滚");
                    }
                }
                return ApiResponseDto<bool>.CreateSuccess(deduct, deduct ? "转账成功" : "转账失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transferring asset from user {FromUserId} to user {ToUserId}", fromUserId, request.ToUserId);
                return ApiResponseDto<bool>.CreateError("转账失败");
            }
        }

        public Task<ApiResponseDto<bool>> RefillSystemAssetAsync(string symbol, decimal amount)
            => Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "系统资产补充功能暂未实现"));

        public Task<ApiResponseDto<bool>> UpdateSystemAssetConfigAsync(string symbol, decimal minReserve, decimal targetBalance, bool autoRefillEnabled)
            => Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "系统资产配置功能暂未实现"));

        public async Task<ApiResponseDto<decimal>> GetTotalAssetValueAsync(int userId, string quoteCurrency = "USDT")
        {
            try
            {
                var assets = await GetUserAssetsInternalAsync(userId);
                return ApiResponseDto<decimal>.CreateSuccess(assets.Sum(a => a.Total));
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
                var assets = (await GetUserAssetsInternalAsync(userId)).Where(a => a.Total >= threshold);
                return ApiResponseDto<IEnumerable<AssetDto>>.CreateSuccess(_mappingService.MapToDto(assets));
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
                var ok = await HasSufficientBalanceInternalAsync(userId, symbol, amount, includeFrozen);
                return ApiResponseDto<bool>.CreateSuccess(ok);
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
                var asset = await GetUserAssetInternalAsync(userId, symbol);
                return ApiResponseDto<bool>.CreateSuccess(asset != null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking asset exists for user {UserId} symbol {Symbol}", userId, symbol);
                return ApiResponseDto<bool>.CreateError("检查资产失败");
            }
        }

        public async Task<ApiResponseDto<bool>> InitializeUserAssetsAsync(int userId, Dictionary<string, decimal> initialBalances)
        {
            try
            {
                foreach (var kv in initialBalances)
                {
                    await AddAssetInternalAsync(userId, kv.Key, kv.Value);
                }
                return ApiResponseDto<bool>.CreateSuccess(true, "初始化成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing user assets for user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("初始化资产失败");
            }
        }
        #endregion

        #region 内部领域操作（私有）
        private Task<IEnumerable<Asset>> GetUserAssetsInternalAsync(int userId)
            => _assetRepository.FindAsync(a => a.UserId == userId);

        private async Task<Asset?> GetUserAssetInternalAsync(int userId, string symbol)
        {
            var assets = await _assetRepository.FindAsync(a => a.UserId == userId && a.Symbol == symbol);
            return assets.FirstOrDefault();
        }

        private async Task<Asset> CreateUserAssetInternalAsync(int userId, string symbol, decimal available = 0, decimal frozen = 0)
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

        private async Task<bool> HasSufficientBalanceInternalAsync(int userId, string symbol, decimal amount, bool includeFrozen = false)
        {
            var asset = await GetUserAssetInternalAsync(userId, symbol);
            if (asset == null) return false;
            var balance = includeFrozen ? asset.Total : asset.Available;
            return balance >= amount;
        }

        private async Task<bool> FreezeAssetInternalAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetUserAssetInternalAsync(userId, symbol);
            if (asset == null) return false; // 资产不存在,冻结失败
            if (asset.Available < amount) return false;
            asset.Available -= amount;
            asset.Frozen += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return true;
        }

        private async Task<bool> UnfreezeAssetInternalAsync(int userId, string symbol, decimal amount)
        {
            var asset = await GetUserAssetInternalAsync(userId, symbol);
            if (asset == null) return false; // 资产不存在,解冻失败
            if (asset.Frozen < amount) return false;
            asset.Frozen -= amount;
            asset.Available += amount;
            asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _assetRepository.UpdateAsync(asset);
            return true;
        }

        private async Task<bool> DeductAssetInternalAsync(int userId, string symbol, decimal amount, bool fromFrozen = false)
        {
            try
            {
                // 使用原子操作避免并发冲突
                int affectedRows;
                if (fromFrozen)
                {
                    affectedRows = await _assetRepository.AtomicDeductFrozenAsync(userId, symbol, amount);
                }
                else
                {
                    affectedRows = await _assetRepository.AtomicDeductAvailableAsync(userId, symbol, amount);
                }
                
                // 如果影响行数为 0,说明资产不足或不存在
                return affectedRows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扣除资产失败: UserId={UserId}, Symbol={Symbol}, Amount={Amount}, FromFrozen={FromFrozen}", 
                    userId, symbol, amount, fromFrozen);
                return false;
            }
        }

        private async Task<bool> AddAssetInternalAsync(int userId, string symbol, decimal amount)
        {
            try
            {
                // 使用原子操作避免并发冲突
                var affectedRows = await _assetRepository.AtomicAddAvailableAsync(userId, symbol, amount);
                
                // 如果影响行数为 0,说明资产不存在,需要创建
                if (affectedRows == 0)
                {
                    // 只有在增加资产时才允许自动创建(充值场景)
                    await CreateUserAssetInternalAsync(userId, symbol, available: amount);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "增加资产失败: UserId={UserId}, Symbol={Symbol}, Amount={Amount}", 
                    userId, symbol, amount);
                return false;
            }
        }
        #endregion
    }
}
