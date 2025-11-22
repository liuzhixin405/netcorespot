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

    public KLineDataRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory, ITradingPairRepository tradingPairRepository, IMemoryCache cache) : base(dbContextFactory)
    {
        _tradingPairRepository = tradingPairRepository;
        _cache = cache;
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var tradingPairId = await ResolveTradingPairIdAsync(symbol);
        return await context.Set<KLineData>().Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
            .OrderByDescending(k => k.OpenTime).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataByTradingPairIdAsync(int tradingPairId, string interval, int limit = 100)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<KLineData>().Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
            .OrderByDescending(k => k.OpenTime).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataByTimeRangeAsync(int tradingPairId, string interval, DateTime startTime, DateTime endTime)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var startMs = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();
        var endMs = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();
        return await context.Set<KLineData>().Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval && k.OpenTime >= startMs && k.CloseTime <= endMs)
            .OrderBy(k => k.OpenTime).ToListAsync();
    }

    public async Task<KLineData?> GetLatestKLineDataAsync(int tradingPairId, string interval)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<KLineData>().Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
            .OrderByDescending(k => k.OpenTime).FirstOrDefaultAsync();
    }

    public async Task<int> SaveKLineDataBatchAsync(IEnumerable<KLineData> klineDataList)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var list = klineDataList.ToList(); if (!list.Any()) return 0;
        var pairIds = list.Select(k => k.TradingPairId).Distinct().ToArray();
        var frames = list.Select(k => k.TimeFrame).Distinct().ToArray();
        var opens = list.Select(k => k.OpenTime).Distinct().ToArray();
        var existing = await context.Set<KLineData>().Where(k => pairIds.Contains(k.TradingPairId) && frames.Contains(k.TimeFrame) && opens.Contains(k.OpenTime))
            .ToDictionaryAsync(k => (k.TradingPairId, k.TimeFrame, k.OpenTime));
        foreach (var item in list)
        {
            var key = (item.TradingPairId, item.TimeFrame, item.OpenTime);
            if (existing.TryGetValue(key, out var exist))
            {
                exist.Open = item.Open; exist.High = item.High; exist.Low = item.Low; exist.Close = item.Close; exist.Volume = item.Volume; exist.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                context.Set<KLineData>().Update(exist);
            }
            else
            {
                await context.Set<KLineData>().AddAsync(item);
            }
        }
        await context.SaveChangesAsync();
        return list.Count;
    }

    public async Task<bool> UpsertKLineDataAsync(KLineData klineData)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var existing = await context.Set<KLineData>().FirstOrDefaultAsync(k => k.TradingPairId == klineData.TradingPairId && k.TimeFrame == klineData.TimeFrame && k.OpenTime == klineData.OpenTime);
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

    public async Task<int> DeleteExpiredKLineDataAsync(int tradingPairId, string interval, int keepDays = 30)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-keepDays).ToUnixTimeMilliseconds();
        var expired = await context.Set<KLineData>().Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval && k.OpenTime < cutoff).ToListAsync();
        if (expired.Any()) context.Set<KLineData>().RemoveRange(expired);
        await context.SaveChangesAsync();
        return expired.Count;
    }

    public async Task<KLineDataStatistics> GetKLineDataStatisticsAsync(int tradingPairId, string interval)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var data = await context.Set<KLineData>().Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval).ToListAsync();
        if (!data.Any()) return new KLineDataStatistics();
        return new KLineDataStatistics
        {
            TotalRecords = data.Count,
            FirstRecordTime = data.Min(k => DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).DateTime),
            LastRecordTime = data.Max(k => DateTimeOffset.FromUnixTimeMilliseconds(k.CloseTime).DateTime),
            HighestPrice = data.Max(k => k.High),
            LowestPrice = data.Min(k => k.Low),
            TotalVolume = data.Sum(k => k.Volume)
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
