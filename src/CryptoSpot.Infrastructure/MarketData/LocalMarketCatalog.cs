namespace CryptoSpot.Infrastructure.MarketData;

/// <summary>
/// 本地演示/开发环境支持 10 个 USDT 交易对。行情订阅、数据库种子和前端下拉都以此为准。
/// </summary>
public sealed record LocalMarketDefinition(
    string Symbol,
    string BaseAsset,
    decimal MinQuantity,
    decimal MaxQuantity,
    int PricePrecision,
    int QuantityPrecision);

public static class LocalMarketCatalog
{
    public static readonly IReadOnlyList<LocalMarketDefinition> Pairs =
    [
        new("BTCUSDT", "BTC", 0.00001m, 1000m, 2, 5),
        new("ETHUSDT", "ETH", 0.001m, 10000m, 2, 3),
        new("BNBUSDT", "BNB", 0.001m, 50000m, 2, 3),
        new("XRPUSDT", "XRP", 0.01m, 10000000m, 4, 2),
        new("ADAUSDT", "ADA", 0.01m, 10000000m, 4, 2),
        new("DOGEUSDT", "DOGE", 0.01m, 100000000m, 5, 2),
        new("SOLUSDT", "SOL", 0.01m, 100000m, 3, 2),
        new("DOTUSDT", "DOT", 0.01m, 1000000m, 3, 2),
        new("LINKUSDT", "LINK", 0.001m, 1000000m, 3, 2),
        new("LTCUSDT", "LTC", 0.001m, 100000m, 2, 3)
    ];

    public static readonly string[] Symbols = Pairs.Select(pair => pair.Symbol).ToArray();

    public static readonly string[] BaseAssets = Pairs.Select(pair => pair.BaseAsset).ToArray();

    /// <summary>本地旧库里可能残留、但交易所已下架的交易对；启动时自动停用。</summary>
    public static readonly string[] ObsoleteSymbols = ["MATICUSDT"];
}
