using CryptoSpot.Domain.Entities;
using CryptoSpot.Bus.Core;

namespace CryptoSpot.Application.DomainCommands.Trading
{
    /// <summary>
    /// 提交订单命令 (移动自 Core 层)
    /// </summary>
    public class SubmitOrderCommand : ICommand<SubmitOrderResult>
    {
        public long UserId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public OrderSide Side { get; set; }
        public OrderType Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal? Price { get; set; }
        public string? ClientOrderId { get; set; }
    }
}
