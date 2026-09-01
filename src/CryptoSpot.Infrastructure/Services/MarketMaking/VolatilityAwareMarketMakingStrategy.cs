using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Options;

namespace CryptoSpot.Infrastructure.Services.MarketMaking;

public sealed class VolatilityAwareMarketMakingStrategy : IMarketMakingStrategy
{
    private readonly MarketMakerOptions _options;

    public VolatilityAwareMarketMakingStrategy(IOptions<MarketMakerOptions> options)
    {
        _options = options.Value;
    }

    public MarketMakingDecision Decide(MarketMakingContext context)
    {
        if (context.CurrentPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(context), "Current price must be positive.");

        var variance = 0m;
        for (var index = 1; index < context.RecentClosePrices.Count; index++)
        {
            var previous = context.RecentClosePrices[index - 1];
            var current = context.RecentClosePrices[index];
            if (previous <= 0 || current <= 0)
                continue;

            var change = (current - previous) / previous;
            variance = _options.EwmaAlpha * change * change + (1 - _options.EwmaAlpha) * variance;
        }

        var sigma = (decimal)Math.Sqrt((double)variance);
        var spreadMultiplier = Clamp(1 + _options.Lambda * (sigma / _options.SigmaReference - 1), 0.5m, 3m);
        var inventoryBias = context.TargetInventory <= 0
            ? 0
            : Clamp(_options.Beta * (context.CurrentInventory - context.TargetInventory) / context.TargetInventory,
                -_options.MaxBias, _options.MaxBias);
        var depthTotal = context.BidDepth + context.AskDepth;
        var depthBias = depthTotal <= 0
            ? 0
            : Clamp(_options.Gamma * (context.BidDepth - context.AskDepth) / depthTotal,
                -_options.MaxBias, _options.MaxBias);
        var bias = Clamp(inventoryBias + depthBias, -_options.MaxBias, _options.MaxBias);
        var halfSpread = _options.BaseSpread * spreadMultiplier;
        var quantityMultiplier = context.RecentTradeCount >= _options.ActiveTradeCount ? 1.25m : 0.75m;
        var quantity = Clamp(_options.BaseOrderSize * quantityMultiplier, _options.MinOrderSize, _options.MaxOrderSize);

        var buy = context.CurrentPrice * (1 - halfSpread + bias);
        var sell = context.CurrentPrice * (1 + halfSpread + bias);
        if (buy <= 0 || sell <= buy)
            throw new InvalidOperationException("Market-making inputs produced an invalid quote.");

        return new MarketMakingDecision
        {
            BuyPrice = buy,
            SellPrice = sell,
            Quantity = quantity,
            Rationale = $"EWMA sigma={sigma:F6}; spreadMultiplier={spreadMultiplier:F3}; inventoryBias={inventoryBias:F6}; depthBias={depthBias:F6}."
        };
    }

    private static decimal Clamp(decimal value, decimal minimum, decimal maximum) => Math.Min(Math.Max(value, minimum), maximum);
}
