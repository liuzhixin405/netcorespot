using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Core.Interfaces.Repositories
{
    /// <summary>
    /// 资产仓储接口
    /// </summary>
    public interface IAssetRepository : IRepository<Asset>
    {
        /// <summary>
        /// 根据用户ID获取资产
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>资产列表</returns>
        Task<IEnumerable<Asset>> GetAssetsByUserIdAsync(int userId);

        /// <summary>
        /// 根据用户ID和资产符号获取资产
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="symbol">资产符号</param>
        /// <returns>资产对象</returns>
        Task<Asset?> GetAssetByUserIdAndSymbolAsync(int userId, string symbol);

        /// <summary>
        /// 更新资产余额
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="symbol">资产符号</param>
        /// <param name="amount">数量变化</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdateBalanceAsync(int userId, string symbol, decimal amount);

        /// <summary>
        /// 冻结资产
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="symbol">资产符号</param>
        /// <param name="amount">冻结数量</param>
        /// <returns>是否冻结成功</returns>
        Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount);

        /// <summary>
        /// 解冻资产
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="symbol">资产符号</param>
        /// <param name="amount">解冻数量</param>
        /// <returns>是否解冻成功</returns>
        Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount);

        /// <summary>
        /// 获取用户资产统计
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>资产统计</returns>
        Task<AssetStatistics> GetAssetStatisticsAsync(int userId);

        /// <summary>
        /// 获取用户资产
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="symbol">资产符号</param>
        /// <returns>资产对象</returns>
        Task<Asset?> GetUserAssetAsync(int userId, string symbol);
    }

    /// <summary>
    /// 资产统计信息
    /// </summary>
    public class AssetStatistics
    {
        public int TotalAssets { get; set; }
        public decimal TotalBalance { get; set; }
        public decimal TotalFrozen { get; set; }
        public decimal TotalAvailable { get; set; }
        public Dictionary<string, decimal> AssetBalances { get; set; } = new();
    }
}
