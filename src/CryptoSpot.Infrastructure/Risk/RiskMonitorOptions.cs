namespace CryptoSpot.Infrastructure.Risk;

public sealed class RiskMonitorOptions
{
    public int ScanIntervalSeconds { get; set; } = 5;
    public decimal FlashCrashPercent { get; set; } = 5m;
    public decimal PriceDeviationPercent { get; set; } = 5m;
    public int OrderFloodThreshold { get; set; } = 20;
    public int WashTradingThreshold { get; set; } = 5;
    public decimal InventoryConcentrationThreshold { get; set; } = 0.8m;
    public int EventDeduplicationSeconds { get; set; } = 60;
}
