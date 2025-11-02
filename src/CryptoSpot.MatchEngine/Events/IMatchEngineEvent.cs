using System;

namespace CryptoSpot.MatchEngine.Events
{
    public interface IMatchEngineEvent
    {
        DateTimeOffset Timestamp { get; }
        string Symbol { get; }
    }
}
