using System.Diagnostics.Metrics;

namespace CryptoSpot.Infrastructure.MarketData;

/// <summary>
/// 行情数据源的连接/消息/队列指标。
/// </summary>
public sealed class MarketDataMetrics
{
    private readonly UpDownCounter<long> _connectionsUp;
    private readonly Counter<long> _reconnects;
    private readonly Counter<long> _messagesReceived;
    private readonly Counter<long> _messagesUnreadable;
    private readonly Counter<long> _tickersReceived;
    private readonly Counter<long> _tickersPushed;
    private readonly Counter<long> _kLinesSaved;
    private readonly Counter<long> _queueDropped;

    public MarketDataMetrics(IMeterFactory meterFactory)
    {
        // [来源: aspnet-core-aspnetcore-10.0#0242 p1516]
        var meter = meterFactory.Create("CryptoSpot.MarketData");

        _connectionsUp = meter.CreateUpDownCounter<long>("cryptospot.marketdata.connections", "{connection}",
            "当前处于已连接状态的行情连接数。");
        _reconnects = meter.CreateCounter<long>("cryptospot.marketdata.reconnects", "{reconnect}",
            "行情连接断开后重新连接的次数。");
        _messagesReceived = meter.CreateCounter<long>("cryptospot.marketdata.messages.received", "{message}",
            "从交易所收到的消息数。");
        _messagesUnreadable = meter.CreateCounter<long>("cryptospot.marketdata.messages.unreadable", "{message}",
            "无法解析的交易所消息数。");
        _tickersReceived = meter.CreateCounter<long>("cryptospot.marketdata.tickers.received", "{ticker}",
            "收到的 ticker 更新数。");
        _tickersPushed = meter.CreateCounter<long>("cryptospot.marketdata.tickers.pushed", "{ticker}",
            "推送给 SignalR 的 ticker 更新数。");
        _kLinesSaved = meter.CreateCounter<long>("cryptospot.marketdata.klines.saved", "{kline}",
            "写入数据库的 K 线数。");
        _queueDropped = meter.CreateCounter<long>("cryptospot.marketdata.queue.dropped", "{item}",
            "因队列已满被丢弃的行情条目数。");
    }

    public void ConnectionUp(string endpoint) => _connectionsUp.Add(1,
        new KeyValuePair<string, object?>("cryptospot.endpoint", endpoint));

    public void ConnectionDown(string endpoint) => _connectionsUp.Add(-1,
        new KeyValuePair<string, object?>("cryptospot.endpoint", endpoint));

    public void Reconnect(string endpoint, string reason) => _reconnects.Add(1,
        new KeyValuePair<string, object?>("cryptospot.endpoint", endpoint),
        new KeyValuePair<string, object?>("cryptospot.failure", reason));

    public void MessageReceived(string endpoint) => _messagesReceived.Add(1,
        new KeyValuePair<string, object?>("cryptospot.endpoint", endpoint));

    public void MessageUnreadable(string endpoint) => _messagesUnreadable.Add(1,
        new KeyValuePair<string, object?>("cryptospot.endpoint", endpoint));

    public void TickerReceived() => _tickersReceived.Add(1);

    public void TickerPushed() => _tickersPushed.Add(1);

    public void KLineSaved() => _kLinesSaved.Add(1);

    public void QueueDropped() => _queueDropped.Add(1);
}
