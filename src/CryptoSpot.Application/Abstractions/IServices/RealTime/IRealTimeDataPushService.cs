using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Users;

namespace CryptoSpot.Application.Abstractions.Services.RealTime
{
    public interface IRealTimeDataPushService
    {
        Task PushKLineDataAsync(string symbol, string interval, KLineDataDto klineData, bool isNewKLine = false);
        Task PushPriceDataAsync(string symbol, object priceData);
        Task PushPriceDataToMultipleSymbolsAsync(Dictionary<string, object> priceUpdates);
        Task PushOrderBookDataAsync(string symbol, OrderBookDepthDto orderBookDepth);
        Task PushOrderBookDeltaAsync(string symbol, List<OrderBookLevelDto> bidChanges, List<OrderBookLevelDto> askChanges);
        Task PushExternalOrderBookSnapshotAsync(string symbol, IReadOnlyList<OrderBookLevelDto> bids, IReadOnlyList<OrderBookLevelDto> asks, long timestamp);
        Task PushLastTradeAndMidPriceAsync(string symbol, decimal? lastPrice, decimal? lastQuantity, decimal? bestBid, decimal? bestAsk, decimal? midPrice, long timestamp);
        Task PushTradeDataAsync(string symbol, MarketTradeDto trade);
        
        // 用户相关推送
        Task PushUserOrderUpdateAsync(int userId, OrderDto order);
        Task PushUserTradeAsync(int userId, TradeDto trade);
        Task PushUserAssetUpdateAsync(int userId, IEnumerable<AssetDto> assets);
    }
}
