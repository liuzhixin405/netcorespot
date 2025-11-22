using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;

namespace CryptoSpot.Application.Features.Trading.CancelOrder
{
    /// <summary>
    /// 取消订单命令
    /// </summary>
    public record CancelOrderCommand(long OrderId) : ICommand<Result<CancelOrderResponse>>;

    /// <summary>
    /// 取消订单响应
    /// </summary>
    public record CancelOrderResponse(
        long OrderId,
        string Message
    );
}
