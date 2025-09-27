namespace CryptoSpot.Application.Abstractions.Trading
{
    public interface IOrderBookSnapshotCache
    {
        void Update(string symbol, IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp);
        (IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp)? Get(string symbol);
        Task<bool> TryLoadAsync(string symbol, CancellationToken ct = default);
    }

    // Removed duplicate OrderBookLevel definition. Use OrderBookLevel declared in IOrderMatchingEngine.
}
