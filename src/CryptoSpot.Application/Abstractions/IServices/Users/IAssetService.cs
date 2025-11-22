using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Application.Abstractions.Services.Users
{
    /// <summary>
    /// 资产服务接口（仅暴露 DTO 层契约，不再暴露领域实体）。
    /// </summary>
    public interface IAssetService
    {
        Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(long userId);
        Task<ApiResponseDto<AssetDto?>> GetUserAssetAsync(long userId, string symbol);
        Task<ApiResponseDto<bool>> AddAssetAsync(long userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> DeductAssetAsync(long userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> FreezeAssetAsync(long userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> UnfreezeAssetAsync(long userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> ConsumeFrozenAssetAsync(long userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> TransferAssetAsync(long fromUserId, AssetTransferRequestDto request);
        Task<ApiResponseDto<bool>> InitializeUserAssetsAsync(long userId, Dictionary<string, decimal> initialBalances);
    }
}
