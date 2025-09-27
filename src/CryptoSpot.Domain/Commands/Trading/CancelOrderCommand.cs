using CryptoSpot.Bus.Core;

namespace CryptoSpot.Domain.Commands.Trading
{
    /// <summary>
    /// 取消订单命令
    /// </summary>
    public class CancelOrderCommand : ICommand<CancelOrderResult>
    {
        public int UserId { get; set; }
        public int OrderId { get; set; }
    }

}
