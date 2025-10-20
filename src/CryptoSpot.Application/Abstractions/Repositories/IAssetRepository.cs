using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Repositories
{
    public interface IAssetRepository : IRepository<Asset>
    {
        Task<IEnumerable<Asset>> GetAssetsByUserIdAsync(int userId);
        Task<Asset?> GetAssetByUserIdAndSymbolAsync(int userId, string symbol);
        Task<bool> UpdateBalanceAsync(int userId, string symbol, decimal amount);
        Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount);
        Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount);
        Task<AssetStatistics> GetAssetStatisticsAsync(int userId);
        Task<Asset?> GetUserAssetAsync(int userId, string symbol);
        
        // 原子操作方法 - 使用数据库级别的更新避免并发冲突
        Task<int> AtomicDeductFrozenAsync(int userId, string symbol, decimal amount);
        Task<int> AtomicAddAvailableAsync(int userId, string symbol, decimal amount);
        Task<int> AtomicDeductAvailableAsync(int userId, string symbol, decimal amount);
    }

    public class AssetStatistics
    {
        public int TotalAssets { get; set; }
        public decimal TotalBalance { get; set; }
        public decimal TotalFrozen { get; set; }
        public decimal TotalAvailable { get; set; }
        public Dictionary<string, decimal> AssetBalances { get; set; } = new();
    }
}
