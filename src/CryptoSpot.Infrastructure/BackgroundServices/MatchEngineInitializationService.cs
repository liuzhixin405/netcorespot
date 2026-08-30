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