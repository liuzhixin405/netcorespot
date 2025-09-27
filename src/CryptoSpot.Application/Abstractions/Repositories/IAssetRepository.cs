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
