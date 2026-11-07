using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Services.MarketData
{
    public interface IMarketDataStreamProvider
    {
        string ProviderName { get; }
        bool HasStarted { get; }
        bool IsConnected { get; }
        DateTimeOffset? LastMessageAt { get; }

        Task ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        Task SubscribeBatchAsync(IReadOnlyList<string> symbols, int depth, string klineInterval = "1m", CancellationToken cancellationToken = default);
        Task UnsubscribeBatchAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default);

        Task SubscribeTickerAsync(string symbol, CancellationToken cancellationToken = default);
        Task SubscribeOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken = default);
        Task SubscribeTradesAsync(string symbol, CancellationToken cancellationToken = default);
        Task SubscribeKLineAsync(string symbol, string interval, CancellationToken cancellationToken = default);

        event Action<MarketTicker>? OnTicker;
        event Action<OrderBookDelta>? OnOrderBook;
        event Action<PublicTrade>? OnTrade;
        event Action<KLineUpdate>? OnKLine;
    }

    public record MarketTicker(string Symbol, decimal Last, decimal High24h, decimal Low24h, decimal Volume24h, decimal ChangePercent, long Ts);

    public record OrderBookDelta(string Symbol, bool IsSnapshot,
        IReadOnlyList<OrderBookLevel> Bids,
        IReadOnlyList<OrderBookLevel> Asks,
        long Ts,
        long? Sequence = null,
        string? RawChecksum = null);

    public record PublicTrade(string Symbol, long TradeId, decimal Price, decimal Quantity, string Side, long Ts);

    public record KLineUpdate(string Symbol, string Interval, long OpenTime, long CloseTime, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, bool IsClosed, long Ts);
}
