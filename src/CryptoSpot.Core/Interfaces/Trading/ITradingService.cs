using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.Trading
{
    public interface ITradingService
    {
        Task<IEnumerable<TradingPair>> GetTradingPairsAsync();
        Task<TradingPair?> GetTradingPairAsync(string symbol);
        Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string timeFrame, int limit = 100);
        Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId);
        Task<IEnumerable<Order>> GetUserOrdersAsync(int userId, string? symbol = null);
        Task<IEnumerable<Trade>> GetUserTradesAsync(int userId, string? symbol = null);
        Task<Order?> SubmitOrderAsync(int userId, SubmitOrderRequest request);
        Task<bool> CancelOrderAsync(int userId, long orderId);
    }

    public class SubmitOrderRequest
    {
        public string Symbol { get; set; } = string.Empty;
        public OrderSide Side { get; set; }
        public OrderType Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal? Price { get; set; }
    }
}
