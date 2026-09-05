using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CryptoSpot.Persistence.Repositories;

public class OrderRepository : BaseRepository<Order>, IOrderRepository
{
    private readonly ITradingPairRepository _tradingPairRepository;
    private readonly IMemoryCache _cache;
    private static readonly string TradingPairCachePrefix = "TradingPairId:";

    public OrderRepository(
        ApplicationDbContext dbContext,
        ITradingPairRepository tradingPairRepository,
        IMemoryCache cache) : base(dbContext)
    {
        _tradingPairRepository = tradingPairRepository;
        _cache = cache;
    }

    public async Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null)
    {
        IQueryable<Order> query = _dbContext.Set<Order>()
            .AsNoTracking()
            .Include(o => o.TradingPair)
            .Where(o => o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled || o.Status == OrderStatus.Pending);

        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(o => o.TradingPairId == tradingPairId);
        }
        return await query.OrderBy(o => o.Side == OrderSide.Buy ? o.Price : -o.Price).ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetUserOrdersAsync(long userId, string? symbol = null, OrderStatus? status = null, int limit = 100)
    {
        var query = _dbContext.Set<Order>().AsNoTracking().Include(o => o.TradingPair).Where(o => o.UserId == userId);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(o => o.TradingPairId == tradingPairId);
        }
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        return await query.OrderByDescending(o => o.CreatedAt).Take(limit).ToListAsync();
    }

    public async Task<Order?> GetOrderByOrderIdStringAsync(string orderIdString)
    {
        return await _dbContext.Set<Order>().AsNoTracking().Include(o => o.TradingPair)
            .FirstOrDefaultAsync(o => o.OrderId == orderIdString);
    }

    public async Task<IEnumerable<Order>> GetOrdersForOrderBookAsync(string symbol, OrderSide side, int depth)
    {
        var tradingPairId = await ResolveTradingPairIdAsync(symbol);
        return await _dbContext.Set<Order>().AsNoTracking()
            .Where(o => o.TradingPairId == tradingPairId && o.Side == side && o.Status == OrderStatus.Active)
            .OrderBy(o => side == OrderSide.Buy ? o.Price : -o.Price)
            .Take(depth).ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetOrdersByUserIdAsync(long userId, string? symbol = null, OrderStatus? status = null)
    {
        var query = _dbContext.Set<Order>().AsNoTracking().Include(o => o.TradingPair).Where(o => o.UserId == userId);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(o => o.TradingPairId == tradingPairId);
        }
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        return await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetActiveOrdersByTradingPairIdAsync(long tradingPairId)
    {
        return await _dbContext.Set<Order>().AsNoTracking()
            .Where(o => o.TradingPairId == tradingPairId && (o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled))
            .OrderBy(o => o.Side == OrderSide.Buy ? o.Price : -o.Price).ToListAsync();
    }

    public async Task<OrderBookDepth> GetOrderBookDepthAsync(long tradingPairId, int depth = 20)
    {
        var bids = await _dbContext.Set<Order>()
            .Where(o => o.TradingPairId == tradingPairId && o.Side == OrderSide.Buy
                     && (o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled))
            .GroupBy(o => o.Price)
            .Select(g => new OrderBookLevel
            {
                Price = g.Key ?? 0,
                Quantity = g.Sum(o => o.Quantity - o.FilledQuantity),
                Total = g.Sum(o => o.Quantity - o.FilledQuantity)
            })
            .OrderByDescending(x => x.Price)
            .Take(depth)
            .ToListAsync();

        var asks = await _dbContext.Set<Order>()
            .Where(o => o.TradingPairId == tradingPairId && o.Side == OrderSide.Sell
                     && (o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled))
            .GroupBy(o => o.Price)
            .Select(g => new OrderBookLevel
            {
                Price = g.Key ?? 0,
                Quantity = g.Sum(o => o.Quantity - o.FilledQuantity),
                Total = g.Sum(o => o.Quantity - o.FilledQuantity)
            })
            .OrderBy(x => x.Price)
            .Take(depth)
            .ToListAsync();

        return new OrderBookDepth { Bids = bids, Asks = asks };
    }

    public async Task<bool> UpdateOrderStatusAsync(long orderId, OrderStatus status, decimal? filledQuantity = null, decimal? averagePrice = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rows = await _dbContext.Set<Order>()
            .Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, status)
                .SetProperty(o => o.FilledQuantity, o => filledQuantity ?? o.FilledQuantity)
                .SetProperty(o => o.AveragePrice, o => averagePrice ?? o.AveragePrice)
                .SetProperty(o => o.UpdatedAt, now));
        return rows > 0;
    }

    public async Task<IEnumerable<Order>> GetUserOrderHistoryAsync(long userId, string? symbol = null, int limit = 100)
    {
        var query = _dbContext.Set<Order>().AsNoTracking().Include(o => o.TradingPair).Where(o => o.UserId == userId);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(o => o.TradingPairId == tradingPairId);
        }
        return await query.OrderByDescending(o => o.CreatedAt).Take(limit).ToListAsync();
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
