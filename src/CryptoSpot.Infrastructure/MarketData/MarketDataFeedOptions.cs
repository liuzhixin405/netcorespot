namespace CryptoSpot.Infrastructure.MarketData;

/// <summary>
/// 市场数据源的连接与批处理参数。按 <c>MarketData</c> 配置节绑定。
/// </summary>
public sealed class MarketDataFeedOptions
{
    public const string Section = "MarketData";

    /// <summary>一条 WebSocket 连接超过该时长没收到任何消息就视为失联。</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>建立 WebSocket 连接的超时时间。</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>指数退避的最小延迟。</summary>
    public TimeSpan ReconnectDelayMin { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>指数退避的最大延迟。</summary>
    public TimeSpan ReconnectDelayMax { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>连接保持发布状态多久后才算稳定，稳定后重置退避。</summary>
    public TimeSpan StableAfter { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>应用层心跳间隔（OKX 用文本 ping）。</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>活动交易对刷新周期。</summary>
    public int SymbolRefreshSeconds { get; init; } = 300;

    /// <summary>价格更新批处理队列容量。</summary>
    public int PriceQueueCapacity { get; init; } = 10000;
}
