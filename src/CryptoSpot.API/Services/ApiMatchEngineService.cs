using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Repositories.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoSpot.API.Services;

/// <summary>
/// API-side implementation of IMatchEngineService used to satisfy DI for MatchEngineAdapter.
/// This implementation does not perform matching — it persists the order into Redis and publishes the stream message
/// so the standalone MatchEngine can consume and process it.
/// </summary>
public class ApiMatchEngineService : IMatchEngineService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<ApiMatchEngineService> _logger;

    public ApiMatchEngineService(IServiceProvider sp, ILogger<ApiMatchEngineService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(Order order, string symbol)
    {
        if (order == null) throw new ArgumentNullException(nameof(order));
        if (string.IsNullOrEmpty(symbol)) throw new ArgumentNullException(nameof(symbol));

        try
        {
            using var scope = _sp.CreateScope();
            var redisOrders = scope.ServiceProvider.GetRequiredService<RedisOrderRepository>();
            // Persist and publish to Redis stream; Api does not perform matching.
            await redisOrders.CreateOrderAsync(order, symbol);
            return order;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApiMatchEngineService failed to persist order for symbol {Symbol}", symbol);
            throw;
        }
    }
}
