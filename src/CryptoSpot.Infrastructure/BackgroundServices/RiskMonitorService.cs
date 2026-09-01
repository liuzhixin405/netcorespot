using System.Text.Json;
using CryptoSpot.Application.Abstractions.Services.Risk;
using CryptoSpot.Application.DTOs.Risk;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Hubs;
using CryptoSpot.Infrastructure.Risk;
using CryptoSpot.Persistence.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CryptoSpot.Infrastructure.BackgroundServices;

public sealed class RiskMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<TradingHub> _hubContext;
    private readonly ILogger<RiskMonitorService> _logger;
    private readonly RiskMonitorOptions _options;

    public RiskMonitorService(
        IServiceScopeFactory scopeFactory,
        IHubContext<TradingHub> hubContext,
        ILogger<RiskMonitorService> logger,
        IOptions<RiskMonitorOptions> options)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.ScanIntervalSeconds, 1, 300));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Risk monitor scan failed.");
            }
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var riskEvents = scope.ServiceProvider.GetRequiredService<IRiskEventService>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var minuteAgo = now - (long)TimeSpan.FromMinutes(1).TotalMilliseconds;
        var deduplicationWindow = TimeSpan.FromSeconds(Math.Clamp(_options.EventDeduplicationSeconds, 1, 3600));
        var detected = new List<RiskEventInput>();

        var pairs = await dbContext.TradingPairs.AsNoTracking().Where(value => value.IsActive)
            .Select(value => new TradingPairSnapshot
            {
                Id = value.Id,
                Symbol = value.Symbol,
                BaseAsset = value.BaseAsset,
                QuoteAsset = value.QuoteAsset,
                Price = value.Price
            })
            .ToListAsync(cancellationToken);
        var pairById = pairs.ToDictionary(value => value.Id);
        var priceByAsset = pairs.Where(value => value.QuoteAsset == "USDT" && value.Price > 0)
            .GroupBy(value => value.BaseAsset)
            .ToDictionary(group => group.Key, group => group.First().Price, StringComparer.OrdinalIgnoreCase);

        await DetectPriceFlashCrashesAsync(dbContext, pairs, detected, cancellationToken);
        await DetectAbnormalOrdersAsync(dbContext, pairById, priceByAsset, minuteAgo, detected, cancellationToken);
        await DetectOrderFloodsAsync(dbContext, minuteAgo, detected, cancellationToken);
        await DetectWashTradingAsync(dbContext, pairById, minuteAgo, detected, cancellationToken);
        await DetectConcentrationsAsync(dbContext, priceByAsset, detected, cancellationToken);

        foreach (var input in detected)
        {
            var eventDto = await riskEvents.RecordIfNewAsync(input, deduplicationWindow, cancellationToken);
            if (eventDto is not null)
                await PublishAsync(eventDto, cancellationToken);
        }
    }

    private async Task DetectPriceFlashCrashesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<TradingPairSnapshot> pairs,
        ICollection<RiskEventInput> detected,
        CancellationToken cancellationToken)
    {
        foreach (var pair in pairs)
        {
            var candles = await dbContext.KLineData.AsNoTracking()
                .Where(value => value.TradingPairId == pair.Id && value.TimeFrame == "1m")
                .OrderByDescending(value => value.OpenTime).Take(2)
                .Select(value => new { value.Close, value.CloseTime }).ToListAsync(cancellationToken);
            if (candles.Count < 2 || candles[1].Close <= 0)
                continue;

            var changePercent = (candles[0].Close - candles[1].Close) / candles[1].Close * 100m;
            if (changePercent > -_options.FlashCrashPercent)
                continue;

            detected.Add(new RiskEventInput(
                null,
                RiskEventType.PriceDeviation,
                RiskSeverity.Critical,
                $"{pair.Symbol} 在一分钟内下跌 {Math.Abs(changePercent):F2}%，建议评估熔断。",
                pair.Symbol,
                MetaJson: JsonSerializer.Serialize(new { previousClose = candles[1].Close, currentClose = candles[0].Close, changePercent })));
        }
    }

    private async Task DetectAbnormalOrdersAsync(
        ApplicationDbContext dbContext,
        IReadOnlyDictionary<long, TradingPairSnapshot> pairById,
        IReadOnlyDictionary<string, decimal> priceByAsset,
        long minuteAgo,
        ICollection<RiskEventInput> detected,
        CancellationToken cancellationToken)
    {
        var orders = await dbContext.Orders.AsNoTracking().Where(value => value.UserId != null && value.CreatedAt >= minuteAgo)
            .Select(value => new { value.Id, UserId = value.UserId!.Value, value.TradingPairId, value.Price, value.Quantity })
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
            return;

        var userIds = orders.Select(value => value.UserId).Distinct().ToList();
        var users = await dbContext.Users.AsNoTracking().Where(value => userIds.Contains(value.Id))
            .Select(value => new { value.Id, value.MaxRiskRatio }).ToDictionaryAsync(value => value.Id, cancellationToken);
        var assets = await dbContext.Assets.AsNoTracking().Where(value => value.UserId != null && userIds.Contains(value.UserId.Value))
            .Select(value => new { UserId = value.UserId!.Value, value.Symbol, Total = value.Available + value.Frozen })
            .ToListAsync(cancellationToken);
        var portfolioValues = assets.GroupBy(value => value.UserId).ToDictionary(
            group => group.Key,
            group => group.Sum(value => ToUsdt(value.Symbol, value.Total, priceByAsset)));

        foreach (var order in orders)
        {
            if (!users.TryGetValue(order.UserId, out var user) || !pairById.TryGetValue(order.TradingPairId, out var pair))
                continue;
            var price = order.Price ?? pair.Price;
            var portfolio = portfolioValues.GetValueOrDefault(order.UserId);
            var notional = order.Quantity * price;
            if (order.Price.HasValue && pair.Price > 0)
            {
                var deviationPercent = Math.Abs(order.Price.Value - pair.Price) / pair.Price * 100m;
                if (deviationPercent > _options.PriceDeviationPercent)
                {
                    detected.Add(new RiskEventInput(
                        order.UserId,
                        RiskEventType.PriceDeviation,
                        RiskSeverity.High,
                        $"订单价格偏离当前市价 {deviationPercent:F2}%，超过允许范围。",
                        pair.Symbol,
                        order.Id,
                        JsonSerializer.Serialize(new { orderPrice = order.Price, marketPrice = pair.Price, deviationPercent })));
                }
            }
            if (price <= 0 || portfolio <= 0 || notional <= portfolio * user.MaxRiskRatio)
                continue;

            detected.Add(new RiskEventInput(
                order.UserId,
                RiskEventType.AbnormalOrder,
                notional > portfolio * user.MaxRiskRatio * 2 ? RiskSeverity.High : RiskSeverity.Medium,
                $"订单金额 {notional:F2} USDT 超过账户风险阈值。",
                pair.Symbol,
                order.Id,
                JsonSerializer.Serialize(new { notional, portfolio, maxRiskRatio = user.MaxRiskRatio })));
        }
    }

    private async Task DetectOrderFloodsAsync(
        ApplicationDbContext dbContext,
        long minuteAgo,
        ICollection<RiskEventInput> detected,
        CancellationToken cancellationToken)
    {
        var floods = await dbContext.Orders.AsNoTracking().Where(value => value.UserId != null && value.CreatedAt >= minuteAgo)
            .GroupBy(value => value.UserId!.Value)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .Where(value => value.Count >= _options.OrderFloodThreshold)
            .ToListAsync(cancellationToken);
        foreach (var flood in floods)
        {
            detected.Add(new RiskEventInput(
                flood.UserId,
                RiskEventType.OrderFlood,
                RiskSeverity.High,
                $"一分钟内提交 {flood.Count} 笔订单，超过频率阈值。",
                MetaJson: JsonSerializer.Serialize(new { flood.Count, threshold = _options.OrderFloodThreshold })));
        }
    }

    private async Task DetectWashTradingAsync(
        ApplicationDbContext dbContext,
        IReadOnlyDictionary<long, TradingPairSnapshot> pairById,
        long minuteAgo,
        ICollection<RiskEventInput> detected,
        CancellationToken cancellationToken)
    {
        var trades = await dbContext.Trades.AsNoTracking().Where(value => value.ExecutedAt >= minuteAgo)
            .Select(value => new { value.BuyerId, value.SellerId, value.Quantity, value.Price, value.TradingPairId })
            .ToListAsync(cancellationToken);
        var candidates = trades.SelectMany(value => new[] { new { UserId = value.BuyerId, Side = 1, value.Quantity, value.Price, value.TradingPairId },
                new { UserId = value.SellerId, Side = -1, value.Quantity, value.Price, value.TradingPairId } })
            .GroupBy(value => new { value.UserId, value.TradingPairId });
        foreach (var candidate in candidates)
        {
            var buys = candidate.Where(value => value.Side == 1).ToList();
            var sells = candidate.Where(value => value.Side == -1).ToList();
            if (buys.Count + sells.Count < _options.WashTradingThreshold || buys.Count == 0 || sells.Count == 0)
                continue;
            var buyQuantity = buys.Sum(value => value.Quantity);
            var sellQuantity = sells.Sum(value => value.Quantity);
            var maximum = Math.Max(buyQuantity, sellQuantity);
            if (maximum == 0 || Math.Abs(buyQuantity - sellQuantity) / maximum > 0.02m)
                continue;

            detected.Add(new RiskEventInput(
                candidate.Key.UserId,
                RiskEventType.WashTrading,
                RiskSeverity.High,
                "一分钟内买卖成交频繁且净仓位变化接近零，存在对倒嫌疑。",
                pairById.TryGetValue(candidate.Key.TradingPairId, out var pair) ? pair.Symbol : null,
                MetaJson: JsonSerializer.Serialize(new { tradeCount = buys.Count + sells.Count, buyQuantity, sellQuantity })));
        }
    }

    private async Task DetectConcentrationsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyDictionary<string, decimal> priceByAsset,
        ICollection<RiskEventInput> detected,
        CancellationToken cancellationToken)
    {
        var assets = await dbContext.Assets.AsNoTracking().Where(value => value.UserId != null && value.Available + value.Frozen > 0)
            .Select(value => new { UserId = value.UserId!.Value, value.Symbol, Total = value.Available + value.Frozen })
            .ToListAsync(cancellationToken);
        foreach (var userAssets in assets.GroupBy(value => value.UserId))
        {
            var values = userAssets.Select(value => new { value.Symbol, Value = ToUsdt(value.Symbol, value.Total, priceByAsset) }).ToList();
            var total = values.Sum(value => value.Value);
            if (total <= 0)
                continue;
            foreach (var asset in values.Where(value => !string.Equals(value.Symbol, "USDT", StringComparison.OrdinalIgnoreCase)))
            {
                var ratio = asset.Value / total;
                if (ratio < _options.InventoryConcentrationThreshold)
                    continue;
                detected.Add(new RiskEventInput(
                    userAssets.Key,
                    RiskEventType.InventoryConcentration,
                    ratio >= 0.95m ? RiskSeverity.High : RiskSeverity.Medium,
                    $"{asset.Symbol} 占投资组合估值的 {ratio:P1}，超过集中度阈值。",
                    MetaJson: JsonSerializer.Serialize(new { asset = asset.Symbol, ratio, threshold = _options.InventoryConcentrationThreshold })));
            }
        }
    }

    private async Task PublishAsync(RiskEventDto riskEvent, CancellationToken cancellationToken)
    {
        if (riskEvent.UserId.HasValue)
            await _hubContext.Clients.Group($"user_{riskEvent.UserId.Value}").SendAsync("RiskAlert", riskEvent, cancellationToken);
        else
            await _hubContext.Clients.Group("risk_alerts").SendAsync("RiskAlert", riskEvent, cancellationToken);
    }

    private static decimal ToUsdt(string symbol, decimal quantity, IReadOnlyDictionary<string, decimal> priceByAsset) =>
        string.Equals(symbol, "USDT", StringComparison.OrdinalIgnoreCase)
            ? quantity
            : quantity * priceByAsset.GetValueOrDefault(symbol, 1m);

    private sealed class TradingPairSnapshot
    {
        public long Id { get; init; }
        public string Symbol { get; init; } = string.Empty;
        public string BaseAsset { get; init; } = string.Empty;
        public string QuoteAsset { get; init; } = string.Empty;
        public decimal Price { get; init; }
    }
}
