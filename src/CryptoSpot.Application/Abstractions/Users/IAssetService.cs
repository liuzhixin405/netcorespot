using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Users
{
    /// <summary>
    /// 用户资产服务抽象（已重构为 Redis 单一读取/写入，数据库通过后台写回刷新）
    /// </summary>
    public interface IAssetService
    {
        Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId);
        Task<Asset?> GetUserAssetAsync(int userId, string symbol);
        Task<Asset> CreateUserAssetAsync(int userId, string symbol, decimal available = 0, decimal frozen = 0);
        Task<Asset> UpdateAssetBalanceAsync(int userId, string symbol, decimal available, decimal frozen);
        Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount);
        Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount);
        Task<bool> DeductAssetAsync(int userId, string symbol, decimal amount, bool fromFrozen = false);
        Task<bool> AddAssetAsync(int userId, string symbol, decimal amount);
        Task<bool> HasSufficientBalanceAsync(int userId, string symbol, decimal amount, bool includeFrozen = false);
        Task InitializeUserAssetsAsync(int userId, Dictionary<string, decimal> initialBalances);
    }
}
