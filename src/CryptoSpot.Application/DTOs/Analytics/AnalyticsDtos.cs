namespace CryptoSpot.Application.DTOs.Analytics;

public enum StrategyType
{
    MaCross,
    RsiMeanReversion,
    BollingerBreak,
    MacdCross
}

public enum SignalAction
{
    Buy,
    Sell,
    Neutral
}

public sealed class BacktestRequestDto
{
    public string Symbol { get; set; } = string.Empty;
    public string Interval { get; set; } = "1h";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public StrategyType Strategy { get; set; } = StrategyType.MaCross;
    public Dictionary<string, decimal> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public decimal InitialCapital { get; set; } = 1000m;
    public decimal FeeRate { get; set; } = 0.001m;
}

public sealed record BacktestEquityPointDto(DateTime Time, decimal Equity);

public sealed record BacktestTradeDto(DateTime Time, SignalAction Action, decimal Price, decimal Quantity, decimal Fee);

public sealed record BacktestResultDto(
    long Id,
    string Symbol,
    string Interval,
    StrategyType Strategy,
    decimal InitialCapital,
    decimal FinalEquity,
    decimal TotalReturn,
    decimal MaxDrawdown,
    decimal WinRate,
    int TradeCount,
    decimal SharpeRatio,
    IReadOnlyList<BacktestEquityPointDto> EquityCurve,
    IReadOnlyList<BacktestTradeDto> Trades);

public sealed record SignalDto(
    DateTime Time,
    string Symbol,
    string Interval,
    StrategyType Strategy,
    SignalAction Action,
    decimal Price,
    string Reason);

public sealed record StrategyDefinitionDto(
    StrategyType Strategy,
    string Description,
    IReadOnlyDictionary<string, decimal> DefaultParameters);
