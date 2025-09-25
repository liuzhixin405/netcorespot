using CryptoSpot.Bus.Core;

namespace CryptoSpot.Application.DomainCommands.Trading
{
    /// <summary>
    /// 取消订单命令 (迁移自 Core)
    /// </summary>
    public class CancelOrderCommand : ICommand<CancelOrderResult>
    {
        public int UserId { get; set; }
        public int OrderId { get; set; }
    }
}
