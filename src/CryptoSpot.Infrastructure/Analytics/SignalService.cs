using CryptoSpot.Application.Abstractions.Services.Analytics;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Analytics;
using CryptoSpot.Application.DTOs.Analytics;

namespace CryptoSpot.Infrastructure.Analytics;

public sealed class SignalService : ISignalService
{
    private const int Lookback = 600;
    private readonly IKLineDataService _klineDataService;

    public SignalService(IKLineDataService klineDataService)
    {
        _klineDataService = klineDataService;
    }

    public async Task<SignalDto> GetLatestAsync(string symbol, string interval, StrategyType strategy,
        IReadOnlyDictionary<string, decimal>? parameters = null, CancellationToken cancellationToken = default)
    {
        var candles = await GetCandlesAsync(symbol, interval, Lookback);
        return BuildSignal(candles, candles.Count - 1, symbol, interval, strategy, parameters);
    }

    public async Task<IReadOnlyList<SignalDto>> GetHistoryAsync(string symbol, string interval, StrategyType strategy,
        int limit = 100, IReadOnlyDictionary<string, decimal>? parameters = null, CancellationToken cancellationToken = default)
    {
        var cappedLimit = Math.Clamp(limit, 1, 500);
        var candles = await GetCandlesAsync(symbol, interval, Math.Min(2_000, cappedLimit + Lookback));
        var signals = new List<SignalDto>();
        for (var index = 1; index < candles.Count; index++)
        {
            var signal = BuildSignal(candles, index, symbol, interval, strategy, parameters);
            if (signal.Action != SignalAction.Neutral)
                signals.Add(signal);
        }
        return signals.TakeLast(cappedLimit).Reverse().ToList();
    }

    private async Task<IReadOnlyList<CryptoSpot.Application.DTOs.MarketData.KLineDataDto>> GetCandlesAsync(
        string symbol, string interval, int limit)
    {
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(interval))
            throw new ArgumentException("交易对和周期不能为空。");
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var response = await _klineDataService.GetKLineDataAsync(normalizedSymbol, interval, limit);
        var candles = response.Success && response.Data is not null
            ? response.Data.Where(value => value.Close > 0).OrderBy(value => value.OpenTime).ToList()
            : throw new InvalidOperationException(response.Error ?? "无法获取信号 K 线数据。");
        if (candles.Count == 0)
            throw new InvalidOperationException("没有可用于生成信号的 K 线数据。");
        return candles;
    }

    private static SignalDto BuildSignal(
        IReadOnlyList<CryptoSpot.Application.DTOs.MarketData.KLineDataDto> candles,
        int index,
        string symbol,
        string interval,
        StrategyType strategy,
        IReadOnlyDictionary<string, decimal>? parameters)
    {
        var closes = candles.Take(index + 1).Select(value => value.Close).ToArray();
        var (action, reason) = TradingStrategyRules.Evaluate(closes, strategy, parameters);
        var candle = candles[index];
        return new SignalDto(candle.CloseDateTime, symbol.Trim().ToUpperInvariant(), interval, strategy, action, candle.Close, reason);
    }
}
