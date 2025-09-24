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
        Task<bool> CancelOrderAsync(int userId, int orderId);
        // 新增接口
        Task<Order?> GetOrderAsync(int userId, int orderId);
        Task<IEnumerable<Trade>> GetOrderTradesAsync(int userId, int orderId);
        Task<IEnumerable<Order>> GetOpenOrdersAsync(int userId, string? symbol = null);
        Task<bool> CancelAllOrdersAsync(int userId, string? symbol = null);
        Task<OrderBookDepth> GetOrderBookDepthAsync(string symbol, int depth = 20);
        Task<TestOrderResult> TestOrderAsync(int userId, SubmitOrderRequest request);
    }

    public class SubmitOrderRequest
    {
        public string Symbol { get; set; } = string.Empty;
        public OrderSide Side { get; set; }
        public OrderType Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal? Price { get; set; }
        public string? ClientOrderId { get; set; }
    }

    public class TestOrderResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public decimal? RequiredQuoteAmount { get; set; } // 买单所需报价资产(如USDT)
        public decimal? RequiredBaseAmount { get; set; }  // 卖单所需基础资产数量
        public SubmitOrderRequest? NormalizedRequest { get; set; }
    }
}
