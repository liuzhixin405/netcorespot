using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    public interface IMatchEngineService
    {
        Task<Order> PlaceOrderAsync(Order order, string symbol);
        Task<OrderBookDepthDto?> GetOrderBookAsync(string symbol, int depth = 20);
    }
}
