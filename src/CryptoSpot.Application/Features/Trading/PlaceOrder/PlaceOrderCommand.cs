using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Features.Trading.PlaceOrder
{
    /// <summary>
    /// 下单命令
    /// </summary>
    public record PlaceOrderCommand(
        string Symbol,
        OrderSide Side,
        OrderType Type,
        decimal Price,
        decimal Quantity
    ) : ICommand<Result<PlaceOrderResponse>>;

    /// <summary>
    /// 下单响应
    /// </summary>
    public record PlaceOrderResponse(
        long OrderId,
        string Symbol,
        OrderSide Side,
        OrderType Type,
        decimal Price,
        decimal Quantity,
        OrderStatus Status,
        DateTime CreatedAt
    );
}
