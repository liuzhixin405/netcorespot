using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Repositories
{
    public class TradingPairRepository : BaseRepository<TradingPair>, ITradingPairRepository
    {
        public TradingPairRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<TradingPair?> GetBySymbolAsync(string symbol)
        {
            return await _dbSet
                .FirstOrDefaultAsync(tp => tp.Symbol == symbol);
        }

        public async Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync()
        {
            return await _dbSet
                .Where(tp => tp.IsActive)
                .OrderBy(tp => tp.Symbol)
                .ToListAsync();
        }

        public async Task<bool> UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            var tradingPair = await GetBySymbolAsync(symbol);
            if (tradingPair != null)
            {
                tradingPair.Price = price;
                tradingPair.Change24h = change24h;
                tradingPair.Volume24h = volume24h;
                tradingPair.High24h = high24h;
                tradingPair.Low24h = low24h;
                tradingPair.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                
                _dbSet.Update(tradingPair);
                return true;
            }
            return false;
        }

        public async Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int limit)
        {
            return await _dbSet
                .Where(tp => tp.IsActive)
                .OrderByDescending(tp => tp.Volume24h)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<int> GetTradingPairIdAsync(string symbol)
        {
            var tradingPair = await GetBySymbolAsync(symbol);
            return tradingPair?.Id ?? 0;
        }

        public async Task<IEnumerable<TradingPair>> SearchTradingPairsAsync(string searchTerm, int limit)
        {
            return await _dbSet
                .Where(tp => tp.IsActive && tp.Symbol.Contains(searchTerm))
                .OrderBy(tp => tp.Symbol)
                .Take(limit)
                .ToListAsync();
        }
    }
}