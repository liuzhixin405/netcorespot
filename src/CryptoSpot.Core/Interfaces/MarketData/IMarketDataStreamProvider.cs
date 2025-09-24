using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;

namespace CryptoSpot.Core.Interfaces.MarketData
{
    /// <summary>
    /// 统一的实时行情流接口（面向 WebSocket/推送）
    /// 与现有 IMarketDataProvider (HTTP+定时同步) 并存，后续可逐步融合或替换。
    /// </summary>
    public interface IMarketDataStreamProvider
    {
        string ProviderName { get; }
        Task ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        Task SubscribeTickerAsync(string symbol, CancellationToken cancellationToken = default);
        Task SubscribeOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken = default);
        Task SubscribeTradesAsync(string symbol, CancellationToken cancellationToken = default);
        Task SubscribeKLineAsync(string symbol, string interval, CancellationToken cancellationToken = default);

        // 事件：统一内部 DTO (暂用简化结构，后续可抽 ValueObjects)
        event Action<MarketTicker>? OnTicker;
        event Action<OrderBookDelta>? OnOrderBook;
        event Action<PublicTrade>? OnTrade;
        event Action<KLineUpdate>? OnKLine;

        bool IsConnected { get; }
    }

    public record MarketTicker(string Symbol, decimal Last, decimal High24h, decimal Low24h, decimal Volume24h, decimal ChangePercent, long Ts);

    public record OrderBookDelta(string Symbol, bool IsSnapshot,
        IReadOnlyList<OrderBookLevel> Bids,
        IReadOnlyList<OrderBookLevel> Asks,
        long Ts,
        long? Sequence = null,
        string? RawChecksum = null);

    public record PublicTrade(string Symbol, long TradeId, decimal Price, decimal Quantity, string Side, long Ts);

    public record KLineUpdate(string Symbol, string Interval, long OpenTime, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, bool IsClosed, long Ts);
}
