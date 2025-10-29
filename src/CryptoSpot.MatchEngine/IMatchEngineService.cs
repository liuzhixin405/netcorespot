using CryptoSpot.Domain.Entities;

namespace CryptoSpot.MatchEngine;

public interface IMatchEngineService
{
    /// <summary>
    /// Place order into engine and trigger matching/settlement.
    /// This adapter will delegate to RedisOrderMatchingEngine.PlaceOrderAsync to keep asset atomicity.
    /// </summary>
    Task<Order> PlaceOrderAsync(Order order, string symbol);
}
