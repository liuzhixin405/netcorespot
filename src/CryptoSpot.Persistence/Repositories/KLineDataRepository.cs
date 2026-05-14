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

    public KLineDataRepository(
        ApplicationDbContext dbContext,
        ITradingPairRepository tradingPairRepository,
        IMemoryCache cache) : base(dbContext)
    {
        _tradingPairRepository = tradingPairRepository;
        _cache = cache;
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
    {
        var tradingPairId = await ResolveTradingPairIdAsync(symbol);
        return await _dbContext.Set<KLineData>().AsNoTracking()
            .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
            .OrderByDescending(k => k.OpenTime).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataByTradingPairIdAsync(long tradingPairId, string interval, int limit = 100)
    {
        return await _dbContext.Set<KLineData>().AsNoTracking()
            .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
            .OrderByDescending(k => k.OpenTime).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataByTimeRangeAsync(long tradingPairId, string interval, DateTime startTime, DateTime endTime)
    {
        var startMs = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();
        var endMs = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();
        return await _dbContext.Set<KLineData>().AsNoTracking()
            .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval
                     && k.OpenTime >= startMs && k.CloseTime <= endMs)
            .OrderBy(k => k.OpenTime).ToListAsync();
    }

    public async Task<IEnumerable<KLineData>> GetKLineDataByTimeRangeAsync(long tradingPairId, string interval, DateTime startTime, DateTime endTime, int limit)
    {
        var startMs = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();
        var endMs = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();
        var sanitizedLimit = Math.Max(1, limit);

        var latest = await _dbContext.Set<KLineData>().AsNoTracking()
            .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval
                     && k.OpenTime >= startMs && k.CloseTime <= endMs)
            .OrderByDescending(k => k.OpenTime)
            .Take(sanitizedLimit)
            .ToListAsync();

        return latest.OrderBy(k => k.OpenTime);
    }

    public async Task<KLineData?> GetLatestKLineDataAsync(long tradingPairId, string interval)
    {
        return await _dbContext.Set<KLineData>().AsNoTracking()
            .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
            .OrderByDescending(k => k.OpenTime).FirstOrDefaultAsync();
    }

    public async Task<int> SaveKLineDataBatchAsync(IEnumerable<KLineData> klineDataList)
    {
        var list = klineDataList.ToList();
        if (!list.Any()) return 0;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var count = 0;

        foreach (var item in list)
        {
            var rows = await _dbContext.Set<KLineData>()
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
                _dbContext.Set<KLineData>().Add(item);
            }

            count++;
        }

        await _dbContext.SaveChangesAsync();
        return count;
    }

    public async Task<bool> UpsertKLineDataAsync(KLineData klineData)
    {
        var existing = await _dbContext.Set<KLineData>().AsNoTracking()
            .FirstOrDefaultAsync(k => k.TradingPairId == klineData.TradingPairId
                                   && k.TimeFrame == klineData.TimeFrame
                                   && k.OpenTime == klineData.OpenTime);

        if (existing != null)
        {
            existing.Open = klineData.Open;
            existing.High = klineData.High;
            existing.Low = klineData.Low;
            existing.Close = klineData.Close;
            existing.Volume = klineData.Volume;
            existing.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _dbContext.Set<KLineData>().Update(existing);
        }
        else
        {
            await _dbContext.Set<KLineData>().AddAsync(klineData);
        }
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<int> DeleteExpiredKLineDataAsync(long tradingPairId, string interval, int keepDays = 30)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-keepDays).ToUnixTimeMilliseconds();
        return await _dbContext.Set<KLineData>()
            .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval && k.OpenTime < cutoff)
            .ExecuteDeleteAsync();
    }

    public async Task<KLineDataStatistics> GetKLineDataStatisticsAsync(long tradingPairId, string interval)
    {
        var result = await _dbContext.Set<KLineData>().AsNoTracking()
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
