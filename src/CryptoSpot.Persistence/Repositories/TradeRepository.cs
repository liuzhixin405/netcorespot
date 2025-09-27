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

    public TradeRepository(ApplicationDbContext context, ITradingPairRepository tradingPairRepository, IMemoryCache cache) : base(context)
    {
        _tradingPairRepository = tradingPairRepository;
        _cache = cache;
    }

    public async Task<IEnumerable<Trade>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100)
    {
        var query = _dbSet.Where(t => t.BuyerId == userId || t.SellerId == userId);
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
            return await _dbSet.OrderByDescending(t => t.ExecutedAt).Take(limit).ToListAsync();
        var tradingPairId = await ResolveTradingPairIdAsync(symbol);
        return await _dbSet.Where(t => t.TradingPairId == tradingPairId).OrderByDescending(t => t.ExecutedAt).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<Trade>> GetTradesByUserIdAsync(int userId, string? symbol = null, int limit = 100)
    {
        var query = _dbSet.Where(t => t.BuyerId == userId || t.SellerId == userId);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(t => t.TradingPairId == tradingPairId);
        }
        return await query.OrderByDescending(t => t.ExecutedAt).Take(limit).ToListAsync();
    }

    public async Task<IEnumerable<Trade>> GetTradesByTradingPairIdAsync(int tradingPairId, int limit = 100) => await _dbSet.Where(t => t.TradingPairId == tradingPairId).OrderByDescending(t => t.ExecutedAt).Take(limit).ToListAsync();

    public async Task<IEnumerable<Trade>> GetTradesByTimeRangeAsync(DateTime startTime, DateTime endTime, string? symbol = null)
    {
        var startMs = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();
        var endMs = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();
        var query = _dbSet.Where(t => t.ExecutedAt >= startMs && t.ExecutedAt <= endMs);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(t => t.TradingPairId == tradingPairId);
        }
        return await query.OrderByDescending(t => t.ExecutedAt).ToListAsync();
    }

    public async Task<TradeStatistics> GetTradeStatisticsAsync(int? userId = null, string? symbol = null, DateTime? startTime = null, DateTime? endTime = null)
    {
        var query = _dbSet.AsQueryable();
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
        var trades = await query.ToListAsync();
        if (!trades.Any()) return new TradeStatistics();
        return new TradeStatistics
        {
            TotalTrades = trades.Count,
            TotalVolume = trades.Sum(t => t.Quantity),
            TotalValue = trades.Sum(t => t.Price * t.Quantity),
            AveragePrice = trades.Average(t => t.Price),
            HighestPrice = trades.Max(t => t.Price),
            LowestPrice = trades.Min(t => t.Price)
        };
    }

    private async Task<int> ResolveTradingPairIdAsync(string symbol)
    {
        var key = TradingPairCachePrefix + symbol;
        if (_cache.TryGetValue<int>(key, out var id)) return id;
        id = await _tradingPairRepository.GetTradingPairIdAsync(symbol);
        _cache.Set(key, id, TimeSpan.FromMinutes(5));
        return id;
    }
}
