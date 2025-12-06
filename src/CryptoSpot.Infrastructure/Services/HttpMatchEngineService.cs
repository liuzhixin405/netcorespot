using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services;

/// <summary>
/// 通过 HTTP 调用独立撮合引擎服务的实现
/// </summary>
public class HttpMatchEngineService : IMatchEngineService
{
    private readonly HttpMatchEngineClient _httpClient;
    private readonly ILogger<HttpMatchEngineService> _logger;

    public HttpMatchEngineService(
        HttpMatchEngineClient httpClient,
        ILogger<HttpMatchEngineService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(Order order, string symbol)
    {
        try
        {
            var result = await _httpClient.SubmitOrderAsync(
                userId: order.UserId ?? 0,
                symbol: symbol,
                side: order.Side == OrderSide.Buy ? "buy" : "sell",
                type: order.Type == OrderType.Market ? "market" : "limit",
                price: order.Price ?? 0,
                quantity: order.Quantity
            );

            // 更新订单状态
            order.OrderId = result.OrderId.ToString();
            order.FilledQuantity = result.ExecutedQuantity;
            
            // 根据成交情况设置订单状态
            if (result.ExecutedQuantity >= order.Quantity)
            {
                order.Status = OrderStatus.Filled;
            }
            else if (result.ExecutedQuantity > 0)
            {
                order.Status = OrderStatus.PartiallyFilled;
            }
            else
            {
                order.Status = OrderStatus.Active;
            }

            _logger.LogInformation(
                "订单已提交到撮合引擎: OrderId={OrderId}, Symbol={Symbol}, Status={Status}, Filled={Filled}/{Total}",
                result.OrderId, symbol, order.Status, order.FilledQuantity, order.Quantity);

            return order;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "提交订单到撮合引擎失败: Symbol={Symbol}", symbol);
            order.Status = OrderStatus.Rejected;
            throw;
        }
    }

    public async Task<Application.DTOs.Trading.OrderBookDepthDto?> GetOrderBookAsync(string symbol, int depth = 20)
    {
        try
        {
            var orderBook = await _httpClient.GetOrderBookAsync(symbol, depth);
            
            if (orderBook != null)
            {
                _logger.LogDebug("Retrieved order book from MatchEngine: {Symbol}, Bids={BidCount}, Asks={AskCount}", 
                    symbol, orderBook.Bids?.Count ?? 0, orderBook.Asks?.Count ?? 0);
            }
            
            return orderBook;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get order book from MatchEngine: Symbol={Symbol}", symbol);
            return null;
        }
    }
}
