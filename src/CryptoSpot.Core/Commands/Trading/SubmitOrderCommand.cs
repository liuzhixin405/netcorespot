using CryptoSpot.Domain.Entities;
using CryptoSpot.Bus.Core;

namespace CryptoSpot.Core.Commands.Trading
{
    /// <summary>
    /// 提交订单命令
    /// </summary>
    public class SubmitOrderCommand : ICommand<SubmitOrderResult>
    {
        public int UserId { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public OrderSide Side { get; set; }
        public OrderType Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal? Price { get; set; }
        public string? ClientOrderId { get; set; }
    }

}
