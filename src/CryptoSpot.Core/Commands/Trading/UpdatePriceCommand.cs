using CryptoSpot.Bus.Core;

namespace CryptoSpot.Core.Commands.Trading
{
    /// <summary>
    /// 更新价格命令 - 用于高频价格更新
    /// </summary>
    public class UpdatePriceCommand : ICommand<UpdatePriceResult>
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Change24h { get; set; }
        public decimal Volume24h { get; set; }
        public decimal High24h { get; set; }
        public decimal Low24h { get; set; }
        public long Timestamp { get; set; }
    }

}
