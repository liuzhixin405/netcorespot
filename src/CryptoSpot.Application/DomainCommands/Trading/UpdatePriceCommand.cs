using CryptoSpot.Bus.Core;

namespace CryptoSpot.Application.DomainCommands.Trading
{
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
