using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.Trading
{
    public interface ITradingPairService
    {
        Task<TradingPair?> GetTradingPairAsync(string symbol);
        Task<int> GetTradingPairIdAsync(string symbol);
        Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync();
        Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int count = 10);
        Task UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);
    }
}
