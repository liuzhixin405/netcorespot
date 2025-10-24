using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Domain.Entities; // 内部仍可使用领域实体
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Infrastructure.Repositories.Redis; // ✅ Redis 仓储

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 资产服务实现 - Redis First 架构（所有操作通过 Redis）
    /// </summary>
    public class AssetService : IAssetService
    {
        // ✅ 使用 Redis 仓储替代数据库仓储
        private readonly RedisAssetRepository _redisAssets;
        private readonly IUnitOfWork _unitOfWork; // 保留用于异常回滚
        private readonly IDtoMappingService _mappingService;
        private readonly ILogger<AssetService> _logger;
        private readonly CryptoSpot.Application.Abstractions.Services.RealTime.IRealTimeDataPushService _realTimePush;

        public AssetService(
            RedisAssetRepository redisAssets, // ✅ 注入 Redis 仓储
            IUnitOfWork unitOfWork,
            IDtoMappingService mappingService,
            ILogger<AssetService> logger,
            CryptoSpot.Application.Abstractions.Services.RealTime.IRealTimeDataPushService realTimePush)
        {
            _redisAssets = redisAssets;
            _unitOfWork = unitOfWork;
            _mappingService = mappingService;
            _logger = logger;
            _realTimePush = realTimePush;
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

        // Removed unused: GetUserAssetSummaryAsync / GetSystemAssetsAsync / GetSystemAssetAsync

        public async Task<ApiResponseDto<bool>> AddAssetAsync(int userId, AssetOperationRequestDto request)
        {
            try
            {
                var ok = await AddAssetInternalAsync(userId, request.Symbol, request.Amount);
                if (ok) await PushUserAssetsSnapshotAsync(userId);
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
                if (ok) await PushUserAssetsSnapshotAsync(userId);
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
                if (ok) await PushUserAssetsSnapshotAsync(userId);
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
                if (ok) await PushUserAssetsSnapshotAsync(userId);
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
                if (ok) await PushUserAssetsSnapshotAsync(userId);
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
                if (deduct)
                {
                    await PushUserAssetsSnapshotAsync(fromUserId);
                    await PushUserAssetsSnapshotAsync(request.ToUserId);
                }
                return ApiResponseDto<bool>.CreateSuccess(deduct, deduct ? "转账成功" : "转账失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transferring asset from user {FromUserId} to user {ToUserId}", fromUserId, request.ToUserId);
                return ApiResponseDto<bool>.CreateError("转账失败");
            }
        }

        // Removed unused extended query / system asset methods

        public async Task<ApiResponseDto<bool>> InitializeUserAssetsAsync(int userId, Dictionary<string, decimal> initialBalances)
        {
            try
            {
                foreach (var kv in initialBalances)
                {
                    await AddAssetInternalAsync(userId, kv.Key, kv.Value);
                }
                await PushUserAssetsSnapshotAsync(userId);
                return ApiResponseDto<bool>.CreateSuccess(true, "初始化成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing user assets for user {UserId}", userId);
                return ApiResponseDto<bool>.CreateError("初始化资产失败");
            }
        }
        #endregion

        #region 内部领域操作（私有 - Redis First）
        
        /// <summary>
        /// 获取用户所有资产 (从 Redis)
        /// </summary>
        private async Task<IEnumerable<Asset>> GetUserAssetsInternalAsync(int userId)
        {
            return await _redisAssets.GetUserAssetsAsync(userId);
        }

        /// <summary>
        /// 获取用户单个资产 (从 Redis)
        /// </summary>
        private async Task<Asset?> GetUserAssetInternalAsync(int userId, string symbol)
        {
            return await _redisAssets.GetAssetAsync(userId, symbol);
        }

        /// <summary>
        /// 创建用户资产 (写入 Redis, 自动同步到 MySQL)
        /// </summary>
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
            
            // ✅ 保存到 Redis (会自动加入同步队列)
            await _redisAssets.SaveAssetAsync(asset);
            
            _logger.LogInformation("✅ 创建资产: UserId={UserId} Symbol={Symbol} Available={Available}", 
                userId, symbol, available);
            
            return asset;
        }

        /// <summary>
        /// 检查余额是否充足 (从 Redis 查询)
        /// </summary>
        private async Task<bool> HasSufficientBalanceInternalAsync(int userId, string symbol, decimal amount, bool includeFrozen = false)
        {
            var asset = await GetUserAssetInternalAsync(userId, symbol);
            if (asset == null) return false;
            var balance = includeFrozen ? asset.Total : asset.Available;
            return balance >= amount;
        }

        /// <summary>
        /// 冻结资产 (Redis 原子操作)
        /// </summary>
        private async Task<bool> FreezeAssetInternalAsync(int userId, string symbol, decimal amount)
        {
            // ✅ 使用 Redis Lua 脚本保证原子性
            var success = await _redisAssets.FreezeAssetAsync(userId, symbol, amount);
            
            if (success)
            {
                _logger.LogDebug("🔒 冻结资产成功: UserId={UserId} Symbol={Symbol} Amount={Amount}", 
                    userId, symbol, amount);
            }
            else
            {
                _logger.LogWarning("⚠️ 冻结资产失败(余额不足): UserId={UserId} Symbol={Symbol} Amount={Amount}",
                    userId, symbol, amount);
            }
            
            return success;
        }

        /// <summary>
        /// 解冻资产 (Redis 原子操作)
        /// </summary>
        private async Task<bool> UnfreezeAssetInternalAsync(int userId, string symbol, decimal amount)
        {
            // ✅ 使用 Redis Lua 脚本保证原子性
            var success = await _redisAssets.UnfreezeAssetAsync(userId, symbol, amount);
            
            if (success)
            {
                _logger.LogDebug("🔓 解冻资产成功: UserId={UserId} Symbol={Symbol} Amount={Amount}", 
                    userId, symbol, amount);
            }
            
            return success;
        }

        /// <summary>
        /// 扣除资产 (Redis 原子操作)
        /// </summary>
        private async Task<bool> DeductAssetInternalAsync(int userId, string symbol, decimal amount, bool fromFrozen = false)
        {
            try
            {
                bool success;
                
                if (fromFrozen)
                {
                    // ✅ 从冻结余额扣除
                    success = await _redisAssets.DeductFrozenAssetAsync(userId, symbol, amount);
                }
                else
                {
                    // 从可用余额扣除 - 先冻结再扣除
                    success = await _redisAssets.FreezeAssetAsync(userId, symbol, amount);
                    if (success)
                    {
                        success = await _redisAssets.DeductFrozenAssetAsync(userId, symbol, amount);
                    }
                }
                
                if (!success)
                {
                    _logger.LogWarning("⚠️ 扣除资产失败: UserId={UserId} Symbol={Symbol} Amount={Amount} FromFrozen={FromFrozen}", 
                        userId, symbol, amount, fromFrozen);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 扣除资产异常: UserId={UserId}, Symbol={Symbol}, Amount={Amount}, FromFrozen={FromFrozen}", 
                    userId, symbol, amount, fromFrozen);
                return false;
            }
        }

        /// <summary>
        /// 增加资产 (Redis 原子操作)
        /// </summary>
        private async Task<bool> AddAssetInternalAsync(int userId, string symbol, decimal amount)
        {
            try
            {
                // ✅ 增加可用余额 (Redis 原子操作)
                var success = await _redisAssets.AddAvailableAssetAsync(userId, symbol, amount);
                
                // 如果资产不存在, 创建新资产
                if (!success)
                {
                    var existingAsset = await GetUserAssetInternalAsync(userId, symbol);
                    if (existingAsset == null)
                    {
                        await CreateUserAssetInternalAsync(userId, symbol, available: amount);
                        return true;
                    }
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 增加资产异常: UserId={UserId}, Symbol={Symbol}, Amount={Amount}", 
                    userId, symbol, amount);
                return false;
            }
        }
        
        // 推送该用户最新资产快照
        private async Task PushUserAssetsSnapshotAsync(int userId)
        {
            try
            {
                var assets = await GetUserAssetsInternalAsync(userId);
                var dto = _mappingService.MapToDto(assets);
                await _realTimePush.PushUserAssetUpdateAsync(userId, dto);
                _logger.LogDebug("[AssetService] 已推送用户资产更新: UserId={UserId}, Count={Count}", userId, dto.Count());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "推送用户资产更新失败: UserId={UserId}", userId);
            }
        }
        #endregion
    }
}
