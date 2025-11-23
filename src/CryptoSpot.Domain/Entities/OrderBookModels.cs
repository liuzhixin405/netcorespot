// Added for refactored matching engine DTO-based flow
namespace CryptoSpot.Domain.Entities
{
    /// <summary>
    /// In-memory order book depth snapshot (not persisted)
    /// </summary>
    [MessagePack.MessagePackObject(true)]
    public class OrderBookDepth
    {
        public string Symbol { get; set; } = string.Empty;
        public List<OrderBookLevel> Bids { get; set; } = new();
        public List<OrderBookLevel> Asks { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    [MessagePack.MessagePackObject(true)]
    public class OrderBookLevel
    {
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public decimal Total { get; set; }
        public int OrderCount { get; set; }
    }
}
