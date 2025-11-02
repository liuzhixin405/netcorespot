using System;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.MatchEngine.Events
{
    public record OrderPlacedEvent(string Symbol, Order Order) : IMatchEngineEvent
    {
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
    }

    public record TradeExecutedEvent(string Symbol, Trade Trade, Order Maker, Order Taker) : IMatchEngineEvent
    {
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
    }

    public record OrderBookChangedEvent(string Symbol) : IMatchEngineEvent
    {
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
    }
}
