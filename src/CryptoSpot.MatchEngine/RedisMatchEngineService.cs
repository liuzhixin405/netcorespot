using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.Application.Abstractions.Services.Trading;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.MatchEngine;

/// <summary>
/// Adapter service in MatchEngine project that delegates to existing RedisOrderMatchingEngine
/// to perform matching and atomic settlement. Keeps MatchEngine as a thin orchestrator now,
/// making it easy to replace with an in-memory matcher later.
/// </summary>
public class RedisMatchEngineService : IMatchEngineService
{
    private readonly RedisOrderMatchingEngine _engine;
    private readonly ILogger<RedisMatchEngineService> _logger;

    public RedisMatchEngineService(RedisOrderMatchingEngine engine, ILogger<RedisMatchEngineService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(Order order, string symbol)
    {
        _logger.LogDebug("RedisMatchEngineService.PlaceOrderAsync delegating to RedisOrderMatchingEngine for OrderId={OrderId}", order.Id);
        var placed = await _engine.PlaceOrderAsync(order, symbol);
        return placed;
    }
}
