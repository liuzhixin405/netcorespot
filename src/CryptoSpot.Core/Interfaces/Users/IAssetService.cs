using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Core.Interfaces.Users
{
    /// <summary>
    /// 资产服务接口
    /// </summary>
    public interface IAssetService
    {
        /// <summary>
        /// 获取用户资产
        /// </summary>
        Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId);
        
        /// <summary>
        /// 获取用户特定资产
        /// </summary>
        Task<Asset?> GetUserAssetAsync(int userId, string symbol);
        
        /// <summary>
        /// 创建用户资产
        /// </summary>
        Task<Asset> CreateUserAssetAsync(int userId, string symbol, decimal available = 0, decimal frozen = 0);
        
        /// <summary>
        /// 更新资产余额
        /// </summary>
        Task<Asset> UpdateAssetBalanceAsync(int userId, string symbol, decimal available, decimal frozen);
        
        /// <summary>
        /// 冻结资产
        /// </summary>
        Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount);
        
        /// <summary>
        /// 解冻资产
        /// </summary>
        Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount);
        
        /// <summary>
        /// 扣除资产
        /// </summary>
        Task<bool> DeductAssetAsync(int userId, string symbol, decimal amount, bool fromFrozen = false);
        
        /// <summary>
        /// 增加资产
        /// </summary>
        Task<bool> AddAssetAsync(int userId, string symbol, decimal amount);
        
        /// <summary>
        /// 检查资产余额是否足够
        /// </summary>
        Task<bool> HasSufficientBalanceAsync(int userId, string symbol, decimal amount, bool includeFrozen = false);
        
        /// <summary>
        /// 初始化用户资产
        /// </summary>
        Task InitializeUserAssetsAsync(int userId, Dictionary<string, decimal> initialBalances);
    }
}
