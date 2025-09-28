using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Domain.Entities; // 新增: 暴露领域实体 Raw 方法

namespace CryptoSpot.Application.Abstractions.Services.Users
{
    /// <summary>
    /// 统一后的资产服务接口 (同时提供 DTO 与 Raw 实体访问)。
    /// </summary>
    public interface IAssetService
    {
        // ================= DTO 层 =================
        Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(int userId);
        Task<ApiResponseDto<AssetDto?>> GetUserAssetAsync(int userId, string symbol);
        Task<ApiResponseDto<AssetSummaryDto>> GetUserAssetSummaryAsync(int userId);
        Task<ApiResponseDto<IEnumerable<AssetDto>>> GetSystemAssetsAsync();
        Task<ApiResponseDto<AssetDto?>> GetSystemAssetAsync(string symbol);
        Task<ApiResponseDto<bool>> AddAssetAsync(int userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> DeductAssetAsync(int userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> FreezeAssetAsync(int userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> UnfreezeAssetAsync(int userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> TransferAssetAsync(int fromUserId, AssetTransferRequestDto request);
        Task<ApiResponseDto<bool>> RefillSystemAssetAsync(string symbol, decimal amount);
        Task<ApiResponseDto<bool>> UpdateSystemAssetConfigAsync(string symbol, decimal minReserve, decimal targetBalance, bool autoRefillEnabled);
        Task<ApiResponseDto<decimal>> GetTotalAssetValueAsync(int userId, string quoteCurrency = "USDT");
        Task<ApiResponseDto<IEnumerable<AssetDto>>> GetAssetsAboveThresholdAsync(int userId, decimal threshold);
        Task<ApiResponseDto<bool>> ValidateAssetOperationAsync(int userId, string symbol, decimal amount, bool includeFrozen = false);
        Task<ApiResponseDto<bool>> CheckAssetExistsAsync(int userId, string symbol);

        // ================= Raw 领域实体方法 =================
        Task<IEnumerable<Asset>> GetUserAssetsRawAsync(int userId);
        Task<Asset?> GetUserAssetRawAsync(int userId, string symbol);
        Task<Asset> CreateUserAssetRawAsync(int userId, string symbol, decimal available = 0, decimal frozen = 0);
        Task<Asset> UpdateAssetBalanceRawAsync(int userId, string symbol, decimal available, decimal frozen);
        Task<bool> HasSufficientBalanceRawAsync(int userId, string symbol, decimal amount, bool includeFrozen = false);
        Task<bool> FreezeAssetRawAsync(int userId, string symbol, decimal amount);
        Task<bool> UnfreezeAssetRawAsync(int userId, string symbol, decimal amount);
        Task<bool> DeductAssetRawAsync(int userId, string symbol, decimal amount, bool fromFrozen = false);
        Task<bool> AddAssetRawAsync(int userId, string symbol, decimal amount);
        Task InitializeUserAssetsRawAsync(int userId, Dictionary<string, decimal> initialBalances);
    }
}
