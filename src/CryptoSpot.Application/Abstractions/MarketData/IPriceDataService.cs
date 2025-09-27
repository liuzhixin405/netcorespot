using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.MarketData
{
    public interface IPriceDataService
    {
        Task<TradingPair?> GetCurrentPriceAsync(string symbol);
        Task<IEnumerable<TradingPair>> GetCurrentPricesAsync(string[] symbols);
        Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int count = 10);
        Task UpdateTradingPairPriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);
        Task BatchUpdateTradingPairPricesAsync(IEnumerable<TradingPair> tradingPairs);
    }
}
