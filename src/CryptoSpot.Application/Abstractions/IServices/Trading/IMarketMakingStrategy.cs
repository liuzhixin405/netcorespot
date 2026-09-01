namespace CryptoSpot.Application.Abstractions.Services.Trading;

public interface IMarketMakingStrategy
{
    MarketMakingDecision Decide(MarketMakingContext context);
}

public sealed record MarketMakingContext
{
    public required string Symbol { get; init; }
    public required decimal CurrentPrice { get; init; }
    public required IReadOnlyList<decimal> RecentClosePrices { get; init; }
    public decimal CurrentInventory { get; init; }
    public decimal TargetInventory { get; init; }
    public decimal BidDepth { get; init; }
    public decimal AskDepth { get; init; }
    public int RecentTradeCount { get; init; }
}

public sealed record MarketMakingDecision
{
    public required decimal BuyPrice { get; init; }
    public required decimal SellPrice { get; init; }
    public required decimal Quantity { get; init; }
    public required string Rationale { get; init; }
}
