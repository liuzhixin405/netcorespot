using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Repositories
{
    public interface ITradingPairRepository : IRepository<TradingPair>
    {
        Task<TradingPair?> GetBySymbolAsync(string symbol);
        Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync();
        Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int limit = 10);
        Task<bool> UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);
        Task<int> GetTradingPairIdAsync(string symbol);
        Task<IEnumerable<TradingPair>> SearchTradingPairsAsync(string keyword, int limit = 20);
    }
}
