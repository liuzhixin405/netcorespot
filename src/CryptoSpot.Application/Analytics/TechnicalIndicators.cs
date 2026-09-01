namespace CryptoSpot.Application.Analytics;

public static class TechnicalIndicators
{
    public static IReadOnlyList<decimal> Sma(IReadOnlyList<decimal> values, int period)
    {
        ValidatePeriod(period);
        var result = new decimal[values.Count];
        decimal sum = 0;
        for (var index = 0; index < values.Count; index++)
        {
            sum += values[index];
            if (index >= period)
                sum -= values[index - period];
            if (index >= period - 1)
                result[index] = sum / period;
        }
        return result;
    }

    public static IReadOnlyList<decimal> Ema(IReadOnlyList<decimal> values, int period)
    {
        ValidatePeriod(period);
        var result = new decimal[values.Count];
        if (values.Count == 0)
            return result;

        var multiplier = 2m / (period + 1);
        result[0] = values[0];
        for (var index = 1; index < values.Count; index++)
            result[index] = values[index] * multiplier + result[index - 1] * (1m - multiplier);
        return result;
    }

    public static IReadOnlyList<decimal> Rsi(IReadOnlyList<decimal> values, int period)
    {
        ValidatePeriod(period);
        var result = new decimal[values.Count];
        if (values.Count <= period)
            return result;

        decimal gains = 0;
        decimal losses = 0;
        for (var index = 1; index <= period; index++)
        {
            var change = values[index] - values[index - 1];
            gains += Math.Max(change, 0);
            losses += Math.Max(-change, 0);
        }

        var averageGain = gains / period;
        var averageLoss = losses / period;
        result[period] = ToRsi(averageGain, averageLoss);
        for (var index = period + 1; index < values.Count; index++)
        {
            var change = values[index] - values[index - 1];
            averageGain = (averageGain * (period - 1) + Math.Max(change, 0)) / period;
            averageLoss = (averageLoss * (period - 1) + Math.Max(-change, 0)) / period;
            result[index] = ToRsi(averageGain, averageLoss);
        }
        return result;
    }

    public static (IReadOnlyList<decimal> Mid, IReadOnlyList<decimal> Upper, IReadOnlyList<decimal> Lower)
        Bollinger(IReadOnlyList<decimal> values, int period, decimal multiplier)
    {
        ValidatePeriod(period);
        if (multiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(multiplier));

        var mid = Sma(values, period).ToArray();
        var deviation = StdDev(values, period);
        var upper = new decimal[values.Count];
        var lower = new decimal[values.Count];
        for (var index = period - 1; index < values.Count; index++)
        {
            upper[index] = mid[index] + multiplier * deviation[index];
            lower[index] = mid[index] - multiplier * deviation[index];
        }
        return (mid, upper, lower);
    }

    public static (IReadOnlyList<decimal> Dif, IReadOnlyList<decimal> Dea, IReadOnlyList<decimal> Hist)
        Macd(IReadOnlyList<decimal> values, int fast, int slow, int signal)
    {
        ValidatePeriod(fast);
        ValidatePeriod(slow);
        ValidatePeriod(signal);
        if (fast >= slow)
            throw new ArgumentOutOfRangeException(nameof(fast), "Fast period must be lower than slow period.");

        var fastEma = Ema(values, fast);
        var slowEma = Ema(values, slow);
        var difference = values.Select((_, index) => fastEma[index] - slowEma[index]).ToArray();
        var dea = Ema(difference, signal);
        var histogram = difference.Select((value, index) => value - dea[index]).ToArray();
        return (difference, dea, histogram);
    }

    public static IReadOnlyList<decimal> StdDev(IReadOnlyList<decimal> values, int period)
    {
        ValidatePeriod(period);
        var result = new decimal[values.Count];
        if (period < 2)
            return result;

        for (var index = period - 1; index < values.Count; index++)
        {
            var mean = 0m;
            for (var valueIndex = index - period + 1; valueIndex <= index; valueIndex++)
                mean += values[valueIndex];
            mean /= period;

            var squaredDifferences = 0m;
            for (var valueIndex = index - period + 1; valueIndex <= index; valueIndex++)
            {
                var difference = values[valueIndex] - mean;
                squaredDifferences += difference * difference;
            }
            result[index] = (decimal)Math.Sqrt((double)(squaredDifferences / (period - 1)));
        }
        return result;
    }

    private static decimal ToRsi(decimal averageGain, decimal averageLoss)
    {
        if (averageLoss == 0)
            return averageGain == 0 ? 50m : 100m;
        return 100m - 100m / (1m + averageGain / averageLoss);
    }

    private static void ValidatePeriod(int period)
    {
        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));
    }
}
