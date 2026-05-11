using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Trading;
using System.Security.Claims;

namespace CryptoSpot.API.Controllers;

[ApiController]
[Route("api/v2/[controller]")]
[Authorize]
public class TradeController : ControllerBase
{
    private readonly ITradingService _tradingService;
    private readonly ILogger<TradeController> _logger;

    public TradeController(
        ITradingService tradingService,
        ILogger<TradeController> logger)
    {
        _tradingService = tradingService;
        _logger = logger;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var userId = GetUserId();

        var dto = new CreateOrderRequestDto
        {
            Symbol = request.Symbol,
            Side = request.Side == "buy" ? Domain.Entities.OrderSide.Buy : Domain.Entities.OrderSide.Sell,
            Type = request.Type == "limit" ? Domain.Entities.OrderType.Limit : Domain.Entities.OrderType.Market,
            Price = request.Price,
            Quantity = request.Quantity
        };

        var result = await _tradingService.SubmitOrderAsync(userId, dto);
        if (!result.Success)
            return BadRequest(new { error = result.Error ?? "Failed to place order" });

        var order = result.Data;
        return Ok(new
        {
            orderId = order?.Id,
            symbol = request.Symbol,
            side = request.Side,
            type = request.Type,
            price = request.Price,
            quantity = request.Quantity,
            status = "submitted",
            createdAt = order?.CreatedAt
        });
    }

    [HttpDelete("orders/{orderId}")]
    public async Task<IActionResult> CancelOrder(long orderId)
    {
        var userId = GetUserId();
        var result = await _tradingService.CancelOrderAsync(userId, orderId);
        if (!result.Success)
            return BadRequest(new { error = result.Error ?? "Failed to cancel order" });
        return Ok(new { message = "Cancel request submitted", orderId });
    }

    [HttpGet("orderbook/{symbol}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetOrderBook(string symbol, [FromQuery] int depth = 20)
    {
        var result = await _tradingService.GetOrderBookDepthAsync(symbol, depth);
        if (!result.Success || result.Data == null)
            return NotFound(new { error = "Trading pair not found" });

        var ob = result.Data;
        return Ok(new
        {
            symbol,
            bids = ob.Bids.Select(b => new[] { b.Price, b.Quantity }).ToList(),
            asks = ob.Asks.Select(a => new[] { a.Price, a.Quantity }).ToList(),
            timestamp = ob.Timestamp
        });
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetUserOrders(
        [FromQuery] string? symbol = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        var result = await _tradingService.GetUserOrdersAsync(userId, symbol);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        var orders = result.Data ?? Enumerable.Empty<OrderDto>();

        if (!string.IsNullOrEmpty(status))
        {
            orders = orders.Where(o => string.Equals(o.Status.ToString(), status, StringComparison.OrdinalIgnoreCase));
        }

        var list = orders.ToList();
        var total = list.Count;
        var paged = list.Skip((page - 1) * pageSize).Take(pageSize);

        return Ok(new
        {
            total,
            page,
            pageSize,
            orders = paged.Select(o => new
            {
                id = o.Id,
                symbol = o.Symbol,
                side = o.Side.ToString().ToLower(),
                type = o.Type.ToString().ToLower(),
                price = o.Price,
                quantity = o.Quantity,
                filledQuantity = o.FilledQuantity,
                status = o.Status.ToString(),
                createdAt = o.CreatedAt,
                updatedAt = o.UpdatedAt
            })
        });
    }

    [HttpGet("trades")]
    public async Task<IActionResult> GetUserTrades(
        [FromQuery] string? symbol = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        var result = await _tradingService.GetUserTradesAsync(userId, symbol);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        var trades = result.Data ?? Enumerable.Empty<TradeDto>();

        if (!string.IsNullOrEmpty(symbol))
        {
            trades = trades.Where(t => string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        }

        var list = trades.ToList();
        var total = list.Count;
        var paged = list.Skip((page - 1) * pageSize).Take(pageSize);

        return Ok(new
        {
            total,
            page,
            pageSize,
            trades = paged.Select(t => new
            {
                id = t.Id,
                symbol = t.Symbol,
                price = t.Price,
                quantity = t.Quantity,
                side = t.Side.ToString().ToLower(),
                role = "participant",
                tradeTime = t.ExecutedAt
            })
        });
    }

    private long GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}

public class PlaceOrderRequest
{
    public required string Symbol { get; set; }
    public required string Side { get; set; }
    public required string Type { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
}
