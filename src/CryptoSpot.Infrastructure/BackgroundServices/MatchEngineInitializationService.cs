using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.MatchEngine.Services;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.BackgroundServices;

public sealed class MatchEngineInitializationService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ChannelMatchEngineService matchEngine,
    ILogger<MatchEngineInitializationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var openOrders = await dbContext.Orders
            .AsNoTracking()
            .Include(o => o.TradingPair)
            .Where(o => o.Status == OrderStatus.Pending
                     || o.Status == OrderStatus.Active
                     || o.Status == OrderStatus.PartiallyFilled)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var restored = 0;
        var invalidOrders = openOrders
            .Where(order => order.Type == OrderType.Market
                || (order.Type == OrderType.Limit && (!order.Price.HasValue || order.Price.Value <= 0)))
            .ToList();

        foreach (var order in invalidOrders)
        {
            if (order.TradingPair == null || string.IsNullOrWhiteSpace(order.TradingPair.Symbol))
            {
                continue;
            }

            var remaining = Math.Max(0m, order.Quantity - order.FilledQuantity);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await dbContext.Orders
                .Where(value => value.Id == order.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Status, OrderStatus.Cancelled)
                    .SetProperty(value => value.UpdatedAt, now), cancellationToken);

            if (order.UserId.HasValue && remaining > 0)
            {
                if (order.Side == OrderSide.Sell)
                {
                    await dbContext.Assets
                        .Where(asset => asset.UserId == order.UserId.Value
                                        && asset.Symbol == order.TradingPair.BaseAsset
                                        && asset.Frozen >= remaining)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(asset => asset.Frozen, asset => asset.Frozen - remaining)
                            .SetProperty(asset => asset.Available, asset => asset.Available + remaining)
                            .SetProperty(asset => asset.UpdatedAt, now), cancellationToken);
                }
                else
                {
                    var releaseAmount = order.Type == OrderType.Market
                        ? remaining * (order.Price ?? order.TradingPair.Price)
                        : remaining * (order.Price ?? 0m);

                    if (releaseAmount > 0)
                    {
                        await dbContext.Assets
                            .Where(asset => asset.UserId == order.UserId.Value
                                            && asset.Symbol == order.TradingPair.QuoteAsset
                                            && asset.Frozen >= releaseAmount)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(asset => asset.Frozen, asset => asset.Frozen - releaseAmount)
                                .SetProperty(asset => asset.Available, asset => asset.Available + releaseAmount)
                                .SetProperty(asset => asset.UpdatedAt, now), cancellationToken);
                    }
                }
            }

            logger.LogWarning(
                "Cancelled invalid open order {OrderId} ({Type}, price={Price}) during match engine restore",
                order.OrderId, order.Type, order.Price);
        }

        openOrders = openOrders.Except(invalidOrders).ToList();

        foreach (var order in openOrders)
        {
            if (order.TradingPair == null || string.IsNullOrWhiteSpace(order.TradingPair.Symbol))
            {
                logger.LogWarning("Skipping order {OrderId} during match engine restore because trading pair is missing", order.OrderId);
                continue;
            }

            matchEngine.RestoreOpenOrder(order, order.TradingPair.Symbol);
            restored++;
        }

        logger.LogInformation("Restored {Count} open orders into match engine order books", restored);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}