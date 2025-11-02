using System;

namespace CryptoSpot.MatchEngine.Core
{
    public interface IMatchEngineMetrics
    {
        void ObserveOrderPlaced(string symbol);
        void ObserveMatchAttempt(string symbol, decimal qty, decimal price);
        void ObserveSettlement(string symbol, bool success, long elapsedMs);
    }

    public class NoOpMatchEngineMetrics : IMatchEngineMetrics
    {
        public void ObserveOrderPlaced(string symbol) { }
        public void ObserveMatchAttempt(string symbol, decimal qty, decimal price) { }
        public void ObserveSettlement(string symbol, bool success, long elapsedMs) { }
    }
}
