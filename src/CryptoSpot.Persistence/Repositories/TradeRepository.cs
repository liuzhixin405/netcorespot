using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CryptoSpot.Persistence.Repositories;

public class TradeRepository : BaseRepository<Trade>, ITradeRepository
{
    private readonly ITradingPairRepository _tradingPairRepository;
    private readonly IMemoryCache _cache;
    private static readonly string TradingPairCachePrefix = "TradingPairId:";

    public TradeRepository(
        ApplicationDbContext dbContext,
        ITradingPairRepository tradingPairRepository,
        IMemoryCache cache) : base(dbContext)
    {
        _tradingPairRepository = tradingPairRepository;
        _cache = cache;
    }

    public async Task<IEnumerable<Trade>> GetTradeHistoryAsync(long userId, string? symbol = null, int limit = 100)
    {
        var query = _dbContext.Set<Trade>().AsNoTracking()
            .Where(t => t.BuyerId == userId || t.SellerId == userId);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(t => t.TradingPairId == tradingPairId);
        }
        return await query.OrderByDescending(t => t.ExecutedAt).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<Trade>> GetRecentTradesAsync(string? symbol = null, int limit = 50)
    {
        if (string.IsNullOrEmpty(symbol))
            return await _dbContext.Set<Trade>().AsNoTracking()
                .OrderByDescending(t => t.ExecutedAt).Take(limit).ToListAsync();

        var tradingPairId = await ResolveTradingPairIdAsync(symbol);
        return await _dbContext.Set<Trade>().AsNoTracking()
            .Where(t => t.TradingPairId == tradingPairId)
            .OrderByDescending(t => t.ExecutedAt).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<Trade>> GetRecentTradesByPairIdAsync(long tradingPairId, int limit = 50)
    {
        return await _dbContext.Set<Trade>().AsNoTracking()
            .Where(t => t.TradingPairId == tradingPairId)
            .OrderByDescending(t => t.ExecutedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IEnumerable<Trade>> GetTradesByUserIdAsync(long userId, string? symbol = null, int limit = 100)
    {
        var query = _dbContext.Set<Trade>().AsNoTracking()
            .Where(t => t.BuyerId == userId || t.SellerId == userId);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(t => t.TradingPairId == tradingPairId);
        }
        return await query.OrderByDescending(t => t.ExecutedAt).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<Trade>> GetTradesByTradingPairIdAsync(long tradingPairId, int limit = 100)
    {
        return await _dbContext.Set<Trade>().AsNoTracking()
            .Where(t => t.TradingPairId == tradingPairId)
            .OrderByDescending(t => t.ExecutedAt).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<Trade>> GetTradesByTimeRangeAsync(DateTime startTime, DateTime endTime, string? symbol = null)
    {
        var startMs = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();
        var endMs = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();
        var query = _dbContext.Set<Trade>().AsNoTracking()
            .Where(t => t.ExecutedAt >= startMs && t.ExecutedAt <= endMs);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(t => t.TradingPairId == tradingPairId);
        }
        return await query.OrderByDescending(t => t.ExecutedAt).ToListAsync();
    }

    public async Task<TradeStatistics> GetTradeStatisticsAsync(long? userId = null, string? symbol = null, DateTime? startTime = null, DateTime? endTime = null)
    {
        var query = _dbContext.Set<Trade>().AsNoTracking().AsQueryable();
        if (userId.HasValue)
            query = query.Where(t => t.BuyerId == userId.Value || t.SellerId == userId.Value);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(t => t.TradingPairId == tradingPairId);
        }
        if (startTime.HasValue)
            query = query.Where(t => t.ExecutedAt >= ((DateTimeOffset)startTime.Value).ToUnixTimeMilliseconds());
        if (endTime.HasValue)
            query = query.Where(t => t.ExecutedAt <= ((DateTimeOffset)endTime.Value).ToUnixTimeMilliseconds());

        var count = await query.CountAsync();
        if (count == 0) return new TradeStatistics();

        return new TradeStatistics
        {
            TotalTrades = count,
            TotalVolume = await query.SumAsync(t => t.Quantity),
            TotalValue = await query.SumAsync(t => t.Price * t.Quantity),
            AveragePrice = await query.AverageAsync(t => t.Price),
            HighestPrice = await query.MaxAsync(t => t.Price),
            LowestPrice = await query.MinAsync(t => t.Price)
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
