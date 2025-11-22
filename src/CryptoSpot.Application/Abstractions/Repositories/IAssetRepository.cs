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
        Task<Asset?> GetUserAssetAsync(int userId, string symbol);
    }
}
