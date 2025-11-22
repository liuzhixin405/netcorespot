using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.Abstractions.Repositories;

namespace CryptoSpot.Application.Features.Trading.GetOrders
{
    /// <summary>
    /// 获取订单查询处理器
    /// </summary>
    public class GetOrdersQueryHandler : ICommandHandler<GetOrdersQuery, Result<List<OrderResponse>>>
    {
        private readonly ICurrentUserService _currentUser;
        private readonly IOrderRepository _orderRepository;

        public GetOrdersQueryHandler(
            ICurrentUserService currentUser,
            IOrderRepository orderRepository)
        {
            _currentUser = currentUser;
            _orderRepository = orderRepository;
        }

        public async Task<Result<List<OrderResponse>>> HandleAsync(GetOrdersQuery query, CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated)
                return Result<List<OrderResponse>>.Failure("User is not authenticated");

            // 获取用户的所有订单(使用正确的方法名和参数)
            var orders = await _orderRepository.GetOrdersByUserIdAsync((int)_currentUser.UserId, query.Symbol, query.Status);

            var response = orders.Select(o => new OrderResponse(
                o.Id,
                o.TradingPair.Symbol,
                o.Side,
                o.Type,
                o.Price ?? 0,
                o.Quantity,
                o.FilledQuantity,
                o.Status,
                o.CreatedDateTime
            )).OrderByDescending(o => o.CreatedAt).ToList();

            return Result<List<OrderResponse>>.Success(response);
        }
    }
}
