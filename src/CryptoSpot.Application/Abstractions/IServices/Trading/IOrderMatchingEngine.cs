using CryptoSpot.Application.DTOs.Trading;

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    public interface IOrderMatchingEngine
    {
        Task<OrderMatchResultDto> ProcessOrderAsync(CreateOrderRequestDto orderRequest, long userId = 0);
        Task<List<TradeDto>> MatchOrdersAsync(string symbol);
        Task<OrderBookDepthDto> GetOrderBookDepthAsync(string symbol, int depth = 20);
        Task<bool> CancelOrderAsync(long orderId, long userId = 0);
        Task<bool> CanMatchOrderAsync(OrderDto buyOrder, OrderDto sellOrder);
    }
}
