using CryptoSpot.Application.DTOs.Analytics;

namespace CryptoSpot.Application.Analytics;

public static class TradingStrategyRules
{
    public static (SignalAction Action, string Reason) Evaluate(
        IReadOnlyList<decimal> closes,
        StrategyType strategy,
        IReadOnlyDictionary<string, decimal>? parameters = null)
    {
        if (closes.Count == 0)
            throw new ArgumentException("At least one close price is required.", nameof(closes));

        return strategy switch
        {
            StrategyType.MaCross => EvaluateMaCross(closes, GetPeriod(parameters, "FastPeriod", 5), GetPeriod(parameters, "SlowPeriod", 20)),
            StrategyType.RsiMeanReversion => EvaluateRsi(closes, GetPeriod(parameters, "Period", 14),
                GetValue(parameters, "Oversold", 30m), GetValue(parameters, "Overbought", 70m)),
            StrategyType.BollingerBreak => EvaluateBollinger(closes, GetPeriod(parameters, "Period", 20),
                GetValue(parameters, "Multiplier", 2m)),
            StrategyType.MacdCross => EvaluateMacd(closes, GetPeriod(parameters, "FastPeriod", 12),
                GetPeriod(parameters, "SlowPeriod", 26), GetPeriod(parameters, "SignalPeriod", 9)),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy))
        };
    }

    private static (SignalAction, string) EvaluateMaCross(IReadOnlyList<decimal> closes, int fastPeriod, int slowPeriod)
    {
        if (fastPeriod >= slowPeriod || closes.Count < slowPeriod + 1)
            return (SignalAction.Neutral, "MA 数据不足或参数无效。");

        var fast = TechnicalIndicators.Sma(closes, fastPeriod);
        var slow = TechnicalIndicators.Sma(closes, slowPeriod);
        var current = closes.Count - 1;
        if (fast[current - 1] <= slow[current - 1] && fast[current] > slow[current])
            return (SignalAction.Buy, $"MA{fastPeriod} 上穿 MA{slowPeriod}。");
        if (fast[current - 1] >= slow[current - 1] && fast[current] < slow[current])
            return (SignalAction.Sell, $"MA{fastPeriod} 下穿 MA{slowPeriod}。");
        return (SignalAction.Neutral, $"MA{fastPeriod} 与 MA{slowPeriod} 未发生交叉。");
    }

    private static (SignalAction, string) EvaluateRsi(IReadOnlyList<decimal> closes, int period, decimal oversold, decimal overbought)
    {
        if (oversold <= 0 || overbought >= 100 || oversold >= overbought || closes.Count <= period)
            return (SignalAction.Neutral, "RSI 数据不足或参数无效。");

        var value = TechnicalIndicators.Rsi(closes, period)[^1];
        if (value < oversold)
            return (SignalAction.Buy, $"RSI({period}) 为 {value:F2}，处于超卖区间。");
        if (value > overbought)
            return (SignalAction.Sell, $"RSI({period}) 为 {value:F2}，处于超买区间。");
        return (SignalAction.Neutral, $"RSI({period}) 为 {value:F2}，未触发阈值。");
    }

    private static (SignalAction, string) EvaluateBollinger(IReadOnlyList<decimal> closes, int period, decimal multiplier)
    {
        if (closes.Count < period || multiplier <= 0)
            return (SignalAction.Neutral, "布林带数据不足或参数无效。");

        var (_, upper, lower) = TechnicalIndicators.Bollinger(closes, period, multiplier);
        var close = closes[^1];
        if (close < lower[^1])
            return (SignalAction.Buy, $"收盘价跌破布林带下轨（{lower[^1]:F4}）。");
        if (close > upper[^1])
            return (SignalAction.Sell, $"收盘价突破布林带上轨（{upper[^1]:F4}）。");
        return (SignalAction.Neutral, "收盘价位于布林带区间内。");
    }

    private static (SignalAction, string) EvaluateMacd(IReadOnlyList<decimal> closes, int fastPeriod, int slowPeriod, int signalPeriod)
    {
        if (fastPeriod >= slowPeriod || closes.Count < slowPeriod + 1)
            return (SignalAction.Neutral, "MACD 数据不足或参数无效。");

        var (dif, dea, _) = TechnicalIndicators.Macd(closes, fastPeriod, slowPeriod, signalPeriod);
        var current = closes.Count - 1;
        if (dif[current - 1] <= dea[current - 1] && dif[current] > dea[current])
            return (SignalAction.Buy, "MACD DIF 上穿 DEA。");
        if (dif[current - 1] >= dea[current - 1] && dif[current] < dea[current])
            return (SignalAction.Sell, "MACD DIF 下穿 DEA。");
        return (SignalAction.Neutral, "MACD 未发生交叉。");
    }

    private static decimal GetValue(IReadOnlyDictionary<string, decimal>? parameters, string name, decimal fallback) =>
        parameters is not null && parameters.TryGetValue(name, out var value) ? value : fallback;

    private static int GetPeriod(IReadOnlyDictionary<string, decimal>? parameters, string name, int fallback)
    {
        var value = GetValue(parameters, name, fallback);
        return value is >= 1 and <= 500 && decimal.Truncate(value) == value ? (int)value : -1;
    }
}
