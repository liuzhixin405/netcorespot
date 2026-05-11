using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Repositories
{
    public interface IAssetRepository : IRepository<Asset>
    {
        Task<IEnumerable<Asset>> GetAssetsByUserIdAsync(long userId);
        Task<Asset?> GetAssetByUserIdAndSymbolAsync(long userId, string symbol);
        Task<bool> UpdateBalanceAsync(long userId, string symbol, decimal amount);
        Task<bool> FreezeAssetAsync(long userId, string symbol, decimal amount);
        Task<bool> UnfreezeAssetAsync(long userId, string symbol, decimal amount);
        Task<Asset?> GetUserAssetAsync(long userId, string symbol);
    }
}
