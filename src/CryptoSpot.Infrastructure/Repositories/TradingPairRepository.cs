using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Extensions;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Repositories
{
    public class TradingPairRepository : Repository<TradingPair>, ITradingPairRepository
    {
        public TradingPairRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<TradingPair?> GetBySymbolAsync(string symbol)
        {
            return await _dbSet
                .FirstOrDefaultAsync(tp => tp.Symbol == symbol && tp.IsActive);
        }

        public async Task<IEnumerable<TradingPair>> GetActivePairsAsync()
        {
            return await _dbSet
                .Where(tp => tp.IsActive)
                .OrderBy(tp => tp.Symbol)
                .ToListAsync();
        }

        public async Task<IEnumerable<TradingPair>> GetTopPairsAsync(int count = 5)
        {
            return await _dbSet
                .Where(tp => tp.IsActive)
                .OrderByDescending(tp => tp.Volume24h)
                .Take(count)
                .ToListAsync();
        }

        public async Task UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            var tradingPair = await _dbSet.FirstOrDefaultAsync(tp => tp.Symbol == symbol);
            if (tradingPair != null)
            {
                tradingPair.Price = price;
                tradingPair.Change24h = change24h;
                tradingPair.Volume24h = volume24h;
                tradingPair.High24h = high24h;
                tradingPair.Low24h = low24h;
                tradingPair.LastUpdated = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();

                await _context.SaveChangesAsync();
            }
        }
    }
}
