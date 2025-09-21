using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.Repositories
{
    public interface ITradingPairRepository : IRepository<TradingPair>
    {
        Task<TradingPair?> GetBySymbolAsync(string symbol);
        Task<IEnumerable<TradingPair>> GetActivePairsAsync();
        Task<IEnumerable<TradingPair>> GetTopPairsAsync(int count = 5);
        Task UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);
    }
}
