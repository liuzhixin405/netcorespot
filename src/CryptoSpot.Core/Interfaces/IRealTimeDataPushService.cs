using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces
{
    public interface IRealTimeDataPushService
    {
        Task PushKLineDataAsync(string symbol, string interval, KLineData klineData, bool isNewKLine = false);
        Task PushPriceDataAsync(string symbol, object priceData);
        Task PushPriceDataToMultipleSymbolsAsync(Dictionary<string, object> priceUpdates);
        Task PushOrderBookDataAsync(string symbol, int depth = 20);
    }
}
