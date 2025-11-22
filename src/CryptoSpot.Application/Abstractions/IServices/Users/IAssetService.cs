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
        Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(long userId);
        Task<ApiResponseDto<AssetDto?>> GetUserAssetAsync(long userId, string symbol);
        // Removed unused summary/system asset APIs
        Task<ApiResponseDto<bool>> AddAssetAsync(long userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> DeductAssetAsync(long userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> FreezeAssetAsync(long userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> UnfreezeAssetAsync(long userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> ConsumeFrozenAssetAsync(long userId, AssetOperationRequestDto request); // 新增: 直接消耗冻结余额
        Task<ApiResponseDto<bool>> TransferAssetAsync(long fromUserId, AssetTransferRequestDto request);
        // Removed unused extended/query APIs
        Task<ApiResponseDto<bool>> InitializeUserAssetsAsync(long userId, Dictionary<string, decimal> initialBalances); // 新增: 初始化用户资产
    }
}
