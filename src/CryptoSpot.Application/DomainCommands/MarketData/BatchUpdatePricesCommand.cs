using CryptoSpot.Bus.Core;

namespace CryptoSpot.Application.DomainCommands.MarketData
{
    /// <summary>
    /// 批量更新交易对价格命令（高频场景）
    /// </summary>
    public class BatchUpdatePricesCommand : ICommand<BatchUpdatePricesResult>
    {
        public List<PriceUpdateItem> PriceUpdates { get; set; } = new();
    }
    
    /// <summary>
    /// 单个价格更新项
    /// </summary>
    public class PriceUpdateItem
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Change24h { get; set; }
        public decimal Volume24h { get; set; }
        public decimal High24h { get; set; }
        public decimal Low24h { get; set; }
    }
    
    /// <summary>
    /// 批量价格更新结果
    /// </summary>
    public class BatchUpdatePricesResult
    {
        public bool Success { get; set; }
        public int TotalCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> FailedSymbols { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }
}
