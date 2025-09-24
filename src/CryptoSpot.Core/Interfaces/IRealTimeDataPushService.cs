using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;

namespace CryptoSpot.Core.Interfaces
{
    public interface IRealTimeDataPushService
    {
        Task PushKLineDataAsync(string symbol, string interval, KLineData klineData, bool isNewKLine = false);
        Task PushPriceDataAsync(string symbol, object priceData);
        Task PushPriceDataToMultipleSymbolsAsync(Dictionary<string, object> priceUpdates);
        Task PushOrderBookDataAsync(string symbol, int depth = 20);
        Task PushOrderBookDeltaAsync(string symbol, List<OrderBookLevel> bidChanges, List<OrderBookLevel> askChanges);
        Task PushExternalOrderBookSnapshotAsync(string symbol, IReadOnlyList<OrderBookLevel> bids, IReadOnlyList<OrderBookLevel> asks, long timestamp);
    }
}
