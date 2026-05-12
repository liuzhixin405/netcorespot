using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Persistence.Repositories;

public class TradingPairRepository : BaseRepository<TradingPair>, ITradingPairRepository
{
    private static readonly Func<ApplicationDbContext, string, CancellationToken, Task<TradingPair?>>
        s_getBySymbol = EF.CompileAsyncQuery(
            (ApplicationDbContext db, string symbol, CancellationToken ct) =>
                db.Set<TradingPair>().AsNoTracking().FirstOrDefault(tp => tp.Symbol == symbol));

    public TradingPairRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory) : base(dbContextFactory) { }

    public async Task<TradingPair?> GetBySymbolAsync(string symbol)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await s_getBySymbol(context, symbol, CancellationToken.None);
    }

    public async Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<TradingPair>().AsNoTracking().Where(tp => tp.IsActive).OrderBy(tp => tp.Symbol).ToListAsync();
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
        return await context.Set<TradingPair>().AsNoTracking().Where(tp => tp.IsActive).OrderByDescending(tp => tp.Volume24h).Take(limit).ToListAsync();
    }

    public async Task<long> GetTradingPairIdAsync(string symbol) => (await GetBySymbolAsync(symbol))?.Id ?? 0;

    public async Task<IEnumerable<TradingPair>> SearchTradingPairsAsync(string searchTerm, int limit)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<TradingPair>().AsNoTracking().Where(tp => tp.IsActive && tp.Symbol.Contains(searchTerm)).OrderBy(tp => tp.Symbol).Take(limit).ToListAsync();
    }
}
