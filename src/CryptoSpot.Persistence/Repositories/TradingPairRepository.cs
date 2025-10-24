using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Persistence.Repositories;

public class TradingPairRepository : BaseRepository<TradingPair>, ITradingPairRepository
{
    public TradingPairRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory) : base(dbContextFactory) { }

    public async Task<TradingPair?> GetBySymbolAsync(string symbol)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<TradingPair>().FirstOrDefaultAsync(tp => tp.Symbol == symbol);
    }

    public async Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<TradingPair>().Where(tp => tp.IsActive).OrderBy(tp => tp.Symbol).ToListAsync();
    }

    public async Task<bool> UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rows = await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE TradingPairs SET Price={price}, Change24h={change24h}, Volume24h={volume24h}, High24h={high24h}, Low24h={low24h}, UpdatedAt={now}, LastUpdated={now} WHERE Symbol={symbol} AND IsDeleted=0");
        return rows > 0;
    }

    public async Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int limit)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<TradingPair>().Where(tp => tp.IsActive).OrderByDescending(tp => tp.Volume24h).Take(limit).ToListAsync();
    }

    public async Task<int> GetTradingPairIdAsync(string symbol) => (await GetBySymbolAsync(symbol))?.Id ?? 0;

    public async Task<IEnumerable<TradingPair>> SearchTradingPairsAsync(string searchTerm, int limit)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<TradingPair>().Where(tp => tp.IsActive && tp.Symbol.Contains(searchTerm)).OrderBy(tp => tp.Symbol).Take(limit).ToListAsync();
    }
}
