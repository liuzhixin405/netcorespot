using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Services.Users
{
    /// <summary>
    /// 领域资产服务接口（返回领域实体），供 V2 DTO 服务与订单撮合/下单逻辑使用。
    /// </summary>
    public interface IAssetService
    {
        Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId);
        Task<Asset?> GetUserAssetAsync(int userId, string symbol);
        Task<Asset> CreateUserAssetAsync(int userId, string symbol, decimal available = 0, decimal frozen = 0);
        Task<Asset> UpdateAssetBalanceAsync(int userId, string symbol, decimal available, decimal frozen);
        Task<bool> HasSufficientBalanceAsync(int userId, string symbol, decimal amount, bool includeFrozen = false);
        Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount);
        Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount);
        Task<bool> DeductAssetAsync(int userId, string symbol, decimal amount, bool fromFrozen = false);
        Task<bool> AddAssetAsync(int userId, string symbol, decimal amount);
        Task InitializeUserAssetsAsync(int userId, Dictionary<string, decimal> initialBalances);
    }
}
