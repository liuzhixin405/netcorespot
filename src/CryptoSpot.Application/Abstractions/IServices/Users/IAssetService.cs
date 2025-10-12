using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Application.Abstractions.Services.Users
{
    /// <summary>
    /// 资产服务接口（仅暴露 DTO 层契约，不再暴露领域实体）。
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
        Task<ApiResponseDto<bool>> ConsumeFrozenAssetAsync(int userId, AssetOperationRequestDto request); // 新增: 直接消耗冻结余额
        Task<ApiResponseDto<bool>> TransferAssetAsync(int fromUserId, AssetTransferRequestDto request);
        Task<ApiResponseDto<bool>> RefillSystemAssetAsync(string symbol, decimal amount);
        Task<ApiResponseDto<bool>> UpdateSystemAssetConfigAsync(string symbol, decimal minReserve, decimal targetBalance, bool autoRefillEnabled);
        Task<ApiResponseDto<decimal>> GetTotalAssetValueAsync(int userId, string quoteCurrency = "USDT");
        Task<ApiResponseDto<IEnumerable<AssetDto>>> GetAssetsAboveThresholdAsync(int userId, decimal threshold);
        Task<ApiResponseDto<bool>> ValidateAssetOperationAsync(int userId, string symbol, decimal amount, bool includeFrozen = false);
        Task<ApiResponseDto<bool>> CheckAssetExistsAsync(int userId, string symbol);
        Task<ApiResponseDto<bool>> InitializeUserAssetsAsync(int userId, Dictionary<string, decimal> initialBalances); // 新增: 初始化用户资产
    }
}
