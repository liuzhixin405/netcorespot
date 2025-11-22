using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Features.Trading.GetOrders
{
    /// <summary>
    /// 获取订单查询
    /// </summary>
    public record GetOrdersQuery(
        string? Symbol = null,
        OrderStatus? Status = null
    ) : ICommand<Result<List<OrderResponse>>>;

    /// <summary>
    /// 订单响应
    /// </summary>
    public record OrderResponse(
        long Id,
        string Symbol,
        OrderSide Side,
        OrderType Type,
        decimal Price,
        decimal Quantity,
        decimal FilledQuantity,
        OrderStatus Status,
        DateTime CreatedAt
    );
}
