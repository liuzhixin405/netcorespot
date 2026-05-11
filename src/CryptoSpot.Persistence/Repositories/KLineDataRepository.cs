// filepath: g:\github\netcorespot\src\CryptoSpot.Persistence\Repositories\KLineDataRepository.cs
using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CryptoSpot.Persistence.Repositories;

public class KLineDataRepository : BaseRepository<KLineData>, IKLineDataRepository
{
    private readonly ITradingPairRepository _tradingPairRepository;
    private readonly IMemoryCache _cache;
    private static readonly string TradingPairCachePrefix = "TradingPairId:";

    private static readonly Func<ApplicationDbContext, long, string, int, CancellationToken, Task<List<KLineData>>>
        s_getKLineData = EF.CompileAsyncQuery(
            (ApplicationDbContext db, long tradingPairId, string interval, int limit, CancellationToken ct) =>
                db.Set<KLineData>().AsNoTracking()
                    .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
                    .OrderByDescending(k => k.OpenTime).Take(limit).ToList());

    private static readonly Func<ApplicationDbContext, long, string, long, CancellationToken, Task<KLineData?>>
        s_firstByKey = EF.CompileAsyncQuery(
            (ApplicationDbContext db, long tradingPairId, string interval, long openTime, CancellationToken ct) =>
                db.Set<KLineData>().AsNoTracking()
                    .FirstOrDefault(k => k.TradingPairId == tradingPairId
                                      && k.TimeFrame == interval
                                      && k.OpenTime == openTime));

    public KLineDataRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory, ITradingPairRepository tradingPairRepository, IMemoryCache cache) : base(dbContextFactory)
    {
        _tradingPairRepository = tradingPairRepository;
        _cache = cache;
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
    {
        var tradingPairId = await ResolveTradingPairIdAsync(symbol);
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await s_getKLineData(context, tradingPairId, interval, limit, CancellationToken.None);
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataByTradingPairIdAsync(long tradingPairId, string interval, int limit = 100)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<KLineData>().Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
            .OrderByDescending(k => k.OpenTime).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataByTimeRangeAsync(long tradingPairId, string interval, DateTime startTime, DateTime endTime)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var startMs = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();
        var endMs = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();
        return await context.Set<KLineData>().Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval && k.OpenTime >= startMs && k.CloseTime <= endMs)
            .OrderBy(k => k.OpenTime).ToListAsync();
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataByTimeRangeAsync(long tradingPairId, string interval, DateTime startTime, DateTime endTime, int limit)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var startMs = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();
        var endMs = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();

        var sanitizedLimit = Math.Max(1, limit);
        var latest = await context.Set<KLineData>()
            .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval && k.OpenTime >= startMs && k.CloseTime <= endMs)
            .OrderByDescending(k => k.OpenTime)
            .Take(sanitizedLimit)
            .ToListAsync();

        return latest.OrderBy(k => k.OpenTime);
    }

    public async Task<KLineData?> GetLatestKLineDataAsync(long tradingPairId, string interval)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<KLineData>().Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
            .OrderByDescending(k => k.OpenTime).FirstOrDefaultAsync();
    }

    public async Task<int> SaveKLineDataBatchAsync(IEnumerable<KLineData> klineDataList)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var list = klineDataList.ToList();
        if (!list.Any()) return 0;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var count = 0;

        foreach (var item in list)
        {
            var rows = await context.Set<KLineData>()
                .Where(k => k.TradingPairId == item.TradingPairId
                         && k.TimeFrame == item.TimeFrame
                         && k.OpenTime == item.OpenTime)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(k => k.Open, item.Open)
                    .SetProperty(k => k.High, item.High)
                    .SetProperty(k => k.Low, item.Low)
                    .SetProperty(k => k.Close, item.Close)
                    .SetProperty(k => k.Volume, item.Volume)
                    .SetProperty(k => k.UpdatedAt, now));

            if (rows == 0)
            {
                item.UpdatedAt = now;
                context.Set<KLineData>().Add(item);
            }

            count++;
        }

        await context.SaveChangesAsync();
        return count;
    }

    public async Task<bool> UpsertKLineDataAsync(KLineData klineData)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var existing = await s_firstByKey(context, klineData.TradingPairId, klineData.TimeFrame, klineData.OpenTime, CancellationToken.None);
        if (existing != null)
        {
            existing.Open = klineData.Open; existing.High = klineData.High; existing.Low = klineData.Low; existing.Close = klineData.Close; existing.Volume = klineData.Volume; existing.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            context.Set<KLineData>().Update(existing);
        }
        else
        {
            await context.Set<KLineData>().AddAsync(klineData);
        }
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<int> DeleteExpiredKLineDataAsync(long tradingPairId, string interval, int keepDays = 30)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-keepDays).ToUnixTimeMilliseconds();
        var expired = await context.Set<KLineData>().Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval && k.OpenTime < cutoff).ToListAsync();
        if (expired.Any()) context.Set<KLineData>().RemoveRange(expired);
        await context.SaveChangesAsync();
        return expired.Count;
    }

    public async Task<KLineDataStatistics> GetKLineDataStatisticsAsync(long tradingPairId, string interval)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var result = await context.Set<KLineData>()
            .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalRecords = g.Count(),
                FirstOpenTime = g.Min(k => k.OpenTime),
                LastCloseTime = g.Max(k => k.CloseTime),
                HighestPrice = g.Max(k => k.High),
                LowestPrice = g.Min(k => k.Low),
                TotalVolume = g.Sum(k => k.Volume)
            })
            .FirstOrDefaultAsync();

        if (result == null) return new KLineDataStatistics();

        return new KLineDataStatistics
        {
            TotalRecords = result.TotalRecords,
            FirstRecordTime = DateTimeOffset.FromUnixTimeMilliseconds(result.FirstOpenTime).DateTime,
            LastRecordTime = DateTimeOffset.FromUnixTimeMilliseconds(result.LastCloseTime).DateTime,
            HighestPrice = result.HighestPrice,
            LowestPrice = result.LowestPrice,
            TotalVolume = result.TotalVolume
        };
    }

    private async Task<long> ResolveTradingPairIdAsync(string symbol)
    {
        var key = TradingPairCachePrefix + symbol;
        if (_cache.TryGetValue<long>(key, out var id)) return id;
        id = await _tradingPairRepository.GetTradingPairIdAsync(symbol);
        _cache.Set(key, id, TimeSpan.FromMinutes(5));
        return id;
    }
}
