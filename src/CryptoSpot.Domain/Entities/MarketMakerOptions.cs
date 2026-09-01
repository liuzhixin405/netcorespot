// filepath: g:\\github\\netcorespot\\src\\CryptoSpot.Domain\\Entities\\MarketMakerOptions.cs
namespace CryptoSpot.Domain.Entities
{
    /// <summary>
    /// 多做市账号配置
    /// </summary>
    public class MarketMakerOptions
    {
        public long[] UserIds { get; set; } = System.Array.Empty<long>();
        public decimal BaseSpread { get; set; } = 0.0005m;
        public decimal Lambda { get; set; } = 1m;
        public decimal SigmaReference { get; set; } = 0.002m;
        public decimal EwmaAlpha { get; set; } = 0.2m;
        public decimal Beta { get; set; } = 0.3m;
        public decimal Gamma { get; set; } = 0.2m;
        public decimal MaxBias { get; set; } = 0.002m;
        public decimal BaseOrderSize { get; set; } = 0.05m;
        public decimal MinOrderSize { get; set; } = 0.01m;
        public decimal MaxOrderSize { get; set; } = 0.5m;
        public decimal TargetInventoryUsdt { get; set; } = 1000m;
        public int ActiveTradeCount { get; set; } = 10;
    }
}
