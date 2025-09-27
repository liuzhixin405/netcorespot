using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Trading;

namespace CryptoSpot.Application.Abstractions.RealTime
{
    public interface IRealTimeDataPushService
    {
        Task PushKLineDataAsync(string symbol, string interval, KLineData klineData, bool isNewKLine = false);
        Task PushPriceDataAsync(string symbol, object priceData);
        Task PushPriceDataToMultipleSymbolsAsync(Dictionary<string, object> priceUpdates);
        Task PushOrderBookDataAsync(string symbol, int depth = 20);
        Task PushOrderBookDeltaAsync(string symbol, List<OrderBookLevel> bidChanges, List<OrderBookLevel> askChanges);
        Task PushExternalOrderBookSnapshotAsync(string symbol, IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp);
        // 新增: 推送最新成交价与中间价（顶层买卖价）
        Task PushLastTradeAndMidPriceAsync(string symbol, decimal? lastPrice, decimal? lastQuantity, decimal? bestBid, decimal? bestAsk, decimal? midPrice, long timestamp);
    }
}
