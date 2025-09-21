using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.System
{
    /// <summary>
    /// 系统资产服务接口
    /// </summary>
    public interface ISystemAssetService
    {
        /// <summary>
        /// 获取系统账号的所有资产
        /// </summary>
        Task<IEnumerable<SystemAsset>> GetSystemAssetsAsync(int systemAccountId);
        
        /// <summary>
        /// 获取系统账号的特定资产
        /// </summary>
        Task<SystemAsset?> GetSystemAssetAsync(int systemAccountId, string symbol);
        
        /// <summary>
        /// 创建系统资产
        /// </summary>
        Task<SystemAsset> CreateSystemAssetAsync(int systemAccountId, string symbol, decimal initialBalance = 0);
        
        /// <summary>
        /// 更新资产余额
        /// </summary>
        Task<SystemAsset> UpdateAssetBalanceAsync(int systemAccountId, string symbol, decimal available, decimal frozen);
        
        /// <summary>
        /// 冻结资产（下单时）
        /// </summary>
        Task<bool> FreezeAssetAsync(int systemAccountId, string symbol, decimal amount);
        
        /// <summary>
        /// 解冻资产（取消订单时）
        /// </summary>
        Task<bool> UnfreezeAssetAsync(int systemAccountId, string symbol, decimal amount);
        
        /// <summary>
        /// 扣除资产（成交时）
        /// </summary>
        Task<bool> DeductAssetAsync(int systemAccountId, string symbol, decimal amount, bool fromFrozen = true);
        
        /// <summary>
        /// 增加资产（成交时）
        /// </summary>
        Task<bool> AddAssetAsync(int systemAccountId, string symbol, decimal amount);
        
        /// <summary>
        /// 检查资产余额是否足够
        /// </summary>
        Task<bool> HasSufficientBalanceAsync(int systemAccountId, string symbol, decimal amount);
        
        /// <summary>
        /// 初始化系统账号资产
        /// </summary>
        Task InitializeSystemAssetsAsync(int systemAccountId, Dictionary<string, decimal> initialBalances);
        
        /// <summary>
        /// 自动充值资产
        /// </summary>
        Task<bool> AutoRefillAssetAsync(int systemAccountId, string symbol);
    }
}
