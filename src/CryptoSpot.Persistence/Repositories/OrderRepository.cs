using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Trading; // added for OrderBookDepth/OrderBookLevel
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CryptoSpot.Persistence.Repositories;

public class OrderRepository : BaseRepository<Order>, IOrderRepository
{
    private readonly ITradingPairRepository _tradingPairRepository;
    private readonly IMemoryCache _cache;
    private static readonly string TradingPairCachePrefix = "TradingPairId:";

    public OrderRepository(ApplicationDbContext context, ITradingPairRepository tradingPairRepository, IMemoryCache cache) : base(context)
    {
        _tradingPairRepository = tradingPairRepository;
        _cache = cache;
    }

    public async Task<IEnumerable<Order>> GetActiveOrdersAsync(string? symbol = null)
    {
        IQueryable<Order> query = _dbSet.Include(o => o.TradingPair)
            .Where(o => o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(o => o.TradingPairId == tradingPairId);
        }
        return await query.OrderBy(o => o.Side == OrderSide.Buy ? o.Price : -o.Price).ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, string? symbol = null, OrderStatus? status = null, int limit = 100)
    {
        var query = _dbSet.Include(o => o.TradingPair).Where(o => o.UserId == userId);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(o => o.TradingPairId == tradingPairId);
        }
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        return await query.OrderByDescending(o => o.CreatedAt).Take(limit).ToListAsync();
    }

    public async Task<Order?> GetOrderByOrderIdStringAsync(string orderIdString) => await _dbSet.Include(o => o.TradingPair).FirstOrDefaultAsync(o => o.OrderId == orderIdString);

    public async Task<IEnumerable<Order>> GetOrdersForOrderBookAsync(string symbol, OrderSide side, int depth)
    {
        var tradingPairId = await ResolveTradingPairIdAsync(symbol);
        return await _dbSet.Where(o => o.TradingPairId == tradingPairId && o.Side == side && o.Status == OrderStatus.Active)
            .OrderBy(o => side == OrderSide.Buy ? o.Price : -o.Price).Take(depth).ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetOrdersByUserIdAsync(int userId, string? symbol = null, OrderStatus? status = null)
    {
        var query = _dbSet.Include(o => o.TradingPair).Where(o => o.UserId == userId);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(o => o.TradingPairId == tradingPairId);
        }
        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        return await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetActiveOrdersByTradingPairIdAsync(int tradingPairId) => await _dbSet
        .Where(o => o.TradingPairId == tradingPairId && (o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled))
        .OrderBy(o => o.Side == OrderSide.Buy ? o.Price : -o.Price).ToListAsync();

    public async Task<OrderBookDepth> GetOrderBookDepthAsync(int tradingPairId, int depth = 20)
    {
        var orders = await _dbSet.Where(o => o.TradingPairId == tradingPairId && (o.Status == OrderStatus.Active || o.Status == OrderStatus.PartiallyFilled))
            .OrderBy(o => o.Side == OrderSide.Buy ? o.Price : -o.Price).Take(depth * 2).ToListAsync();
        var bids = orders.Where(o => o.Side == OrderSide.Buy).GroupBy(o => o.Price)
            .Select(g => new OrderBookLevel { Price = g.Key ?? 0, Quantity = g.Sum(o => o.Quantity - o.FilledQuantity), Total = g.Sum(o => o.Quantity - o.FilledQuantity) })
            .OrderByDescending(l => l.Price).Take(depth).ToList();
        var asks = orders.Where(o => o.Side == OrderSide.Sell).GroupBy(o => o.Price)
            .Select(g => new OrderBookLevel { Price = g.Key ?? 0, Quantity = g.Sum(o => o.Quantity - o.FilledQuantity), Total = g.Sum(o => o.Quantity - o.FilledQuantity) })
            .OrderBy(l => l.Price).Take(depth).ToList();
        return new OrderBookDepth { Bids = bids, Asks = asks };
    }

    public async Task<bool> UpdateOrderStatusAsync(int orderId, OrderStatus status, decimal? filledQuantity = null, decimal? averagePrice = null)
    {
        var order = await _dbSet.FindAsync(orderId);
        if (order == null) return false;
        order.Status = status;
        order.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (filledQuantity.HasValue) order.FilledQuantity = filledQuantity.Value;
        if (averagePrice.HasValue) order.AveragePrice = averagePrice.Value;
        _dbSet.Update(order);
        return true;
    }

    public async Task<IEnumerable<Order>> GetUserOrderHistoryAsync(int userId, string? symbol = null, int limit = 100)
    {
        var query = _dbSet.Include(o => o.TradingPair).Where(o => o.UserId == userId);
        if (!string.IsNullOrEmpty(symbol))
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            query = query.Where(o => o.TradingPairId == tradingPairId);
        }
        return await query.OrderByDescending(o => o.CreatedAt).Take(limit).ToListAsync();
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
