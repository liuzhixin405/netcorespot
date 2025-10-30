using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Repositories.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoSpot.API.Services;

/// <summary>
/// API-side producer service: receives order placement calls from application code,
/// persists the order into Redis (via RedisOrderRepository) and publishes the stream/event
/// so the standalone MatchEngine can consume and process it.
///
/// Responsibilities:
/// - Validate input and persist orders to Redis
/// - Publish stream messages / queue entries for downstream MatchEngine
/// - Do NOT perform matching or asset settlement here — that is the responsibility
///   of the independent `CryptoSpot.MatchEngine` service.
///
/// Keep this class lightweight to preserve the separation of concerns between API (producer)
/// and MatchEngine (consumer/processor).
/// </summary>
public class ApiOrderPublisherService : IMatchEngineService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<ApiOrderPublisherService> _logger;

    public ApiOrderPublisherService(IServiceProvider sp, ILogger<ApiOrderPublisherService> logger)
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
            _logger.LogError(ex, "ApiOrderPublisherService failed to persist order for symbol {Symbol}", symbol);
            throw;
        }
    }
}
