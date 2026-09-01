using System.Text.Json;
using CryptoSpot.Application.Abstractions.Services.Analytics;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Analytics;
using CryptoSpot.Application.DTOs.Analytics;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Analytics;

public sealed class BacktestService : IBacktestService
{
    private const int MaxKLines = 10_000;
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IKLineDataService _klineDataService;
    private readonly ApplicationDbContext _dbContext;

    public BacktestService(IKLineDataService klineDataService, ApplicationDbContext dbContext)
    {
        _klineDataService = klineDataService;
        _dbContext = dbContext;
    }

    public async Task<BacktestResultDto> RunAsync(long userId, BacktestRequestDto request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var response = await _klineDataService.GetKLineDataAsync(symbol, request.Interval,
            ToUnixMilliseconds(request.StartTime), ToUnixMilliseconds(request.EndTime), MaxKLines);
        var candles = response.Success && response.Data is not null
            ? response.Data.Where(value => value.Open > 0 && value.Close > 0).OrderBy(value => value.OpenTime).ToList()
            : throw new InvalidOperationException(response.Error ?? "无法获取回测 K 线数据。");
        if (candles.Count < 2)
            throw new InvalidOperationException("回测区间内至少需要两根有效 K 线。");

        var result = Simulate(candles, symbol, request);
        var completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var run = new BacktestRun
        {
            UserId = userId,
            Symbol = symbol,
            Interval = request.Interval,
            Strategy = request.Strategy.ToString(),
            ConfigJson = JsonSerializer.Serialize(request, SerializerOptions),
            ResultJson = JsonSerializer.Serialize(result, SerializerOptions),
            CompletedAt = completedAt
        };
        _dbContext.BacktestRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);

        result = result with { Id = run.Id };
        run.ResultJson = JsonSerializer.Serialize(result, SerializerOptions);
        run.Touch();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<BacktestResultDto?> GetAsync(long userId, long id, CancellationToken cancellationToken = default)
    {
        var resultJson = await _dbContext.BacktestRuns.AsNoTracking()
            .Where(value => value.Id == id && value.UserId == userId && !value.IsDeleted)
            .Select(value => value.ResultJson)
            .SingleOrDefaultAsync(cancellationToken);
        return resultJson is null
            ? null
            : JsonSerializer.Deserialize<BacktestResultDto>(resultJson, SerializerOptions)
                ?? throw new InvalidOperationException("已保存的回测结果无效。");
    }

    public IReadOnlyList<StrategyDefinitionDto> GetStrategies() =>
    [
        new(StrategyType.MaCross, "短周期均线上穿/下穿长周期均线。", new Dictionary<string, decimal> { ["FastPeriod"] = 5, ["SlowPeriod"] = 20 }),
        new(StrategyType.RsiMeanReversion, "RSI 超卖买入、超卖卖出。", new Dictionary<string, decimal> { ["Period"] = 14, ["Oversold"] = 30, ["Overbought"] = 70 }),
        new(StrategyType.BollingerBreak, "收盘价突破布林带上下轨。", new Dictionary<string, decimal> { ["Period"] = 20, ["Multiplier"] = 2 }),
        new(StrategyType.MacdCross, "MACD DIF 与 DEA 交叉。", new Dictionary<string, decimal> { ["FastPeriod"] = 12, ["SlowPeriod"] = 26, ["SignalPeriod"] = 9 })
    ];

    private static BacktestResultDto Simulate(
        IReadOnlyList<CryptoSpot.Application.DTOs.MarketData.KLineDataDto> candles,
        string symbol,
        BacktestRequestDto request)
    {
        var cash = request.InitialCapital;
        var quantity = 0m;
        var entryCost = 0m;
        var wins = 0;
        var closedTrades = 0;
        SignalAction? pendingAction = null;
        var equityCurve = new List<BacktestEquityPointDto>(candles.Count);
        var trades = new List<BacktestTradeDto>();
        var closes = new List<decimal>(candles.Count);

        for (var index = 0; index < candles.Count; index++)
        {
            var candle = candles[index];
            if (pendingAction is SignalAction.Buy && cash > 0)
            {
                var executionPrice = candle.Open;
                var boughtQuantity = cash / (executionPrice * (1m + request.FeeRate));
                var fee = boughtQuantity * executionPrice * request.FeeRate;
                entryCost = cash;
                quantity = boughtQuantity;
                cash = 0;
                trades.Add(new BacktestTradeDto(candle.OpenDateTime, SignalAction.Buy, executionPrice, boughtQuantity, fee));
            }
            else if (pendingAction is SignalAction.Sell && quantity > 0)
            {
                var executionPrice = candle.Open;
                var grossProceeds = quantity * executionPrice;
                var fee = grossProceeds * request.FeeRate;
                cash = grossProceeds - fee;
                if (cash > entryCost)
                    wins++;
                closedTrades++;
                trades.Add(new BacktestTradeDto(candle.OpenDateTime, SignalAction.Sell, executionPrice, quantity, fee));
                quantity = 0;
                entryCost = 0;
            }

            closes.Add(candle.Close);
            var signal = TradingStrategyRules.Evaluate(closes, request.Strategy, request.Parameters);
            pendingAction = signal.Action;
            equityCurve.Add(new BacktestEquityPointDto(candle.CloseDateTime, cash + quantity * candle.Close));
        }

        var finalEquity = equityCurve[^1].Equity;
        return new BacktestResultDto(
            0,
            symbol,
            request.Interval,
            request.Strategy,
            request.InitialCapital,
            finalEquity,
            request.InitialCapital == 0 ? 0 : finalEquity / request.InitialCapital - 1m,
            CalculateMaxDrawdown(equityCurve),
            closedTrades == 0 ? 0 : (decimal)wins / closedTrades,
            trades.Count,
            CalculateSharpe(equityCurve, request.Interval),
            equityCurve,
            trades);
    }

    private static decimal CalculateMaxDrawdown(IReadOnlyList<BacktestEquityPointDto> equityCurve)
    {
        var peak = equityCurve[0].Equity;
        var maximum = 0m;
        foreach (var point in equityCurve)
        {
            peak = Math.Max(peak, point.Equity);
            if (peak > 0)
                maximum = Math.Max(maximum, (peak - point.Equity) / peak);
        }
        return maximum;
    }

    private static decimal CalculateSharpe(IReadOnlyList<BacktestEquityPointDto> equityCurve, string interval)
    {
        if (equityCurve.Count < 3)
            return 0;

        var returns = new List<decimal>(equityCurve.Count - 1);
        for (var index = 1; index < equityCurve.Count; index++)
        {
            var previous = equityCurve[index - 1].Equity;
            if (previous > 0)
                returns.Add(equityCurve[index].Equity / previous - 1m);
        }
        if (returns.Count < 2)
            return 0;

        var average = returns.Average();
        var variance = returns.Sum(value => (value - average) * (value - average)) / (returns.Count - 1);
        if (variance == 0)
            return 0;
        return average / (decimal)Math.Sqrt((double)variance) * (decimal)Math.Sqrt(PeriodsPerYear(interval));
    }

    private static int PeriodsPerYear(string interval) => interval.ToLowerInvariant() switch
    {
        "1m" => 525_600,
        "5m" => 105_120,
        "15m" => 35_040,
        "30m" => 17_520,
        "1h" => 8_760,
        "4h" => 2_190,
        "1d" => 365,
        _ => 365
    };

    private static long ToUnixMilliseconds(DateTime value) =>
        new DateTimeOffset(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value)
            .ToUnixTimeMilliseconds();

    private static void Validate(BacktestRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol) || string.IsNullOrWhiteSpace(request.Interval))
            throw new ArgumentException("交易对和周期不能为空。");
        if (request.StartTime == default || request.EndTime == default || request.StartTime >= request.EndTime)
            throw new ArgumentException("回测时间范围无效。");
        if (request.InitialCapital <= 0 || request.FeeRate is < 0 or > 0.1m)
            throw new ArgumentException("初始资金或手续费率无效。");
    }
}
