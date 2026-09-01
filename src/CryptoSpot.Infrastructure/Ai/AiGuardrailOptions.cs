namespace CryptoSpot.Infrastructure.Ai;

public sealed class AiGuardrailOptions
{
    public decimal MarketOrderApprovalThreshold { get; set; } = 1000m;
    public decimal LimitOrderApprovalThreshold { get; set; } = 5000m;
    public decimal MaxPriceDeviationPercent { get; set; } = 5m;
    public int MaxOrdersPerMinute { get; set; } = 5;
    public decimal DailyTradeApprovalThreshold { get; set; } = 10000m;
}
