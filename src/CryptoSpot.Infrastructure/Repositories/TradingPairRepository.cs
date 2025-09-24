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
            // 高频价格更新对精确并发控制要求低，采用最后写入者获胜策略，绕过 Version 并发列，避免大量 DbUpdateConcurrencyException
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var rows = await _context.Database.ExecuteSqlInterpolatedAsync($@"UPDATE TradingPairs
SET Price={price}, Change24h={change24h}, Volume24h={volume24h}, High24h={high24h}, Low24h={low24h}, UpdatedAt={now}, LastUpdated={now}
WHERE Symbol={symbol} AND IsDeleted=0");
            return rows > 0;

            /* 若需恢复乐观锁控制，可使用旧实现：
            const int maxRetry = 3;
            for (int attempt = 1; attempt <= maxRetry; attempt++)
            {
                try
                {
                    var tradingPair = await GetBySymbolAsync(symbol);
                    if (tradingPair == null) return false;
                    tradingPair.Price = price;
                    tradingPair.Change24h = change24h;
                    tradingPair.Volume24h = volume24h;
                    tradingPair.High24h = high24h;
                    tradingPair.Low24h = low24h;
                    tradingPair.UpdatedAt = now;
                    tradingPair.LastUpdated = now;
                    await _context.SaveChangesAsync();
                    return true;
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (attempt == maxRetry) throw;
                    foreach (var entry in _context.ChangeTracker.Entries<TradingPair>()) entry.State = EntityState.Detached;
                    await Task.Delay(10 * attempt);
                }
            }
            return false;*/
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