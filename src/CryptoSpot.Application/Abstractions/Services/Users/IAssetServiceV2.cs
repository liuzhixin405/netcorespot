using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Application.Abstractions.Services.Users
{
    /// <summary>
    /// 资产服务接口 - 使用DTO
    /// </summary>
    public interface IAssetServiceV2
    {
        // 用户资产查询
        Task<ApiResponseDto<IEnumerable<AssetDto>>> GetUserAssetsAsync(int userId);
        Task<ApiResponseDto<AssetDto?>> GetUserAssetAsync(int userId, string symbol);
        Task<ApiResponseDto<AssetSummaryDto>> GetUserAssetSummaryAsync(int userId);
        
        // 系统资产查询
        Task<ApiResponseDto<IEnumerable<AssetDto>>> GetSystemAssetsAsync();
        Task<ApiResponseDto<AssetDto?>> GetSystemAssetAsync(string symbol);
        
        // 资产操作
        Task<ApiResponseDto<bool>> AddAssetAsync(int userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> DeductAssetAsync(int userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> FreezeAssetAsync(int userId, AssetOperationRequestDto request);
        Task<ApiResponseDto<bool>> UnfreezeAssetAsync(int userId, AssetOperationRequestDto request);
        
        // 资产转移
        Task<ApiResponseDto<bool>> TransferAssetAsync(int fromUserId, AssetTransferRequestDto request);
        
        // 系统资产管理
        Task<ApiResponseDto<bool>> RefillSystemAssetAsync(string symbol, decimal amount);
        Task<ApiResponseDto<bool>> UpdateSystemAssetConfigAsync(string symbol, decimal minReserve, decimal targetBalance, bool autoRefillEnabled);
        
        // 资产统计
        Task<ApiResponseDto<decimal>> GetTotalAssetValueAsync(int userId, string quoteCurrency = "USDT");
        Task<ApiResponseDto<IEnumerable<AssetDto>>> GetAssetsAboveThresholdAsync(int userId, decimal threshold);
        
        // 资产验证
        Task<ApiResponseDto<bool>> ValidateAssetOperationAsync(int userId, string symbol, decimal amount, bool includeFrozen = false);
        Task<ApiResponseDto<bool>> CheckAssetExistsAsync(int userId, string symbol);
    }
}
