using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CryptoSpot.Infrastructure.Matching;
using CryptoSpot.Domain.Matching;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CryptoSpot.API.Controllers;

/// <summary>
/// 简化的交易 API v2
/// 直接与撮合引擎交互，无需 CommandBus
/// </summary>
[ApiController]
[Route("api/v2/[controller]")]
[Authorize]
public class TradeController : ControllerBase
{
    private readonly InMemoryMatchingEngine _matchingEngine;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<TradeController> _logger;

    public TradeController(
        InMemoryMatchingEngine matchingEngine,
        ApplicationDbContext dbContext,
        ILogger<TradeController> logger)
    {
        _matchingEngine = matchingEngine;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// 下单 - 限价单或市价单
    /// </summary>
    [HttpPost("orders")]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var userId = GetUserId();

        try
        {
            // 1. 验证交易对
            var tradingPair = await _dbContext.TradingPairs
                .Include(tp => tp.BaseAsset)
                .Include(tp => tp.QuoteAsset)
                .FirstOrDefaultAsync(tp => tp.Symbol == request.Symbol);

            if (tradingPair == null)
            {
                return BadRequest(new { error = "Invalid trading pair" });
            }

            // 2. 验证并冻结资产
            var (success, message) = await ValidateAndFreezeAssetsAsync(
                userId, 
                tradingPair, 
                request.Side, 
                request.Type,
                request.Price, 
                request.Quantity);

            if (!success)
            {
                return BadRequest(new { error = message });
            }

            // 3. 创建订单记录
            var order = new Domain.Entities.Order
            {
                UserId = userId,
                TradingPairId = tradingPair.Id,
                Side = request.Side == "buy" 
                    ? Domain.Entities.OrderSide.Buy 
                    : Domain.Entities.OrderSide.Sell,
                Type = request.Type == "limit" 
                    ? Domain.Entities.OrderType.Limit 
                    : Domain.Entities.OrderType.Market,
                Price = request.Price,
                Quantity = request.Quantity,
                FilledQuantity = 0,
                Status = Domain.Entities.OrderStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            // 4. 提交到撮合引擎
            var matchOrder = new MatchOrder
            {
                Id = order.Id,
                Symbol = request.Symbol,
                UserId = userId,
                Side = request.Side == "buy" ? Side.Buy : Side.Sell,
                Type = request.Type == "limit" ? OrderType.Limit : OrderType.Market,
                Price = request.Price,
                Size = request.Quantity,
                Funds = request.Type == "market" && request.Side == "buy" 
                    ? request.Price * request.Quantity 
                    : 0,
                Status = "New"
            };

            var submitted = await _matchingEngine.SubmitOrderAsync(matchOrder);

            if (!submitted)
            {
                return StatusCode(500, new { error = "Failed to submit order to matching engine" });
            }

            return Ok(new
            {
                orderId = order.Id,
                symbol = request.Symbol,
                side = request.Side,
                type = request.Type,
                price = request.Price,
                quantity = request.Quantity,
                status = "submitted",
                createdAt = order.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing order for user {UserId}", userId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// 取消订单
    /// </summary>
    [HttpDelete("orders/{orderId}")]
    public async Task<IActionResult> CancelOrder(long orderId)
    {
        var userId = GetUserId();

        try
        {
            var order = await _dbContext.Orders
                .Include(o => o.TradingPair)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

            if (order == null)
            {
                return NotFound(new { error = "Order not found" });
            }

            if (order.Status != Domain.Entities.OrderStatus.Active && 
                order.Status != Domain.Entities.OrderStatus.PartiallyFilled)
            {
                return BadRequest(new { error = "Order cannot be cancelled" });
            }

            // 提交取消请求到撮合引擎
            var matchOrder = new MatchOrder
            {
                Id = order.Id,
                Symbol = order.TradingPair.Symbol,
                UserId = userId,
                Side = order.Side == Domain.Entities.OrderSide.Buy ? Side.Buy : Side.Sell,
                Type = order.Type == Domain.Entities.OrderType.Limit ? OrderType.Limit : OrderType.Market,
                Price = order.Price ?? 0,
                Size = order.Quantity - order.FilledQuantity,
                Status = "Cancelling"
            };

            var submitted = await _matchingEngine.SubmitOrderAsync(matchOrder);

            if (!submitted)
            {
                return StatusCode(500, new { error = "Failed to cancel order" });
            }

            return Ok(new { message = "Cancel request submitted", orderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling order {OrderId}", orderId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// 获取订单簿
    /// </summary>
    [HttpGet("orderbook/{symbol}")]
    [AllowAnonymous]
    public IActionResult GetOrderBook(string symbol)
    {
        var orderBook = _matchingEngine.GetOrderBook(symbol);

        if (orderBook == null)
        {
            return NotFound(new { error = "Trading pair not found" });
        }

        return Ok(new
        {
            symbol = orderBook.Symbol,
            bids = orderBook.Bids.Select(b => new[] { b.Price, b.Size }).ToList(),
            asks = orderBook.Asks.Select(a => new[] { a.Price, a.Size }).ToList(),
            timestamp = orderBook.Timestamp
        });
    }

    /// <summary>
    /// 获取用户订单
    /// </summary>
    [HttpGet("orders")]
    public async Task<IActionResult> GetUserOrders(
        [FromQuery] string? symbol = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();

        var query = _dbContext.Orders
            .Include(o => o.TradingPair)
            .Where(o => o.UserId == userId);

        if (!string.IsNullOrEmpty(symbol))
        {
            query = query.Where(o => o.TradingPair.Symbol == symbol);
        }

        if (!string.IsNullOrEmpty(status))
        {
            var orderStatus = Enum.Parse<Domain.Entities.OrderStatus>(status, true);
            query = query.Where(o => o.Status == orderStatus);
        }

        var total = await query.CountAsync();
        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            total,
            page,
            pageSize,
            orders = orders.Select(o => new
            {
                id = o.Id,
                symbol = o.TradingPair.Symbol,
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

    /// <summary>
    /// 获取成交记录
    /// </summary>
    [HttpGet("trades")]
    public async Task<IActionResult> GetUserTrades(
        [FromQuery] string? symbol = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();

        var query = _dbContext.Trades
            .Include(t => t.TradingPair)
            .Where(t => t.BuyerId == userId || t.SellerId == userId);

        if (!string.IsNullOrEmpty(symbol))
        {
            query = query.Where(t => t.TradingPair.Symbol == symbol);
        }

        var total = await query.CountAsync();
        var trades = await query
            .OrderByDescending(t => t.ExecutedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            total,
            page,
            pageSize,
            trades = trades.Select(t => new
            {
                id = t.Id,
                symbol = t.TradingPair.Symbol,
                price = t.Price,
                quantity = t.Quantity,
                side = t.BuyerId == userId ? "buy" : "sell",
                role = "participant",
                tradeTime = t.ExecutedDateTime
            })
        });
    }

    private async Task<(bool success, string message)> ValidateAndFreezeAssetsAsync(
        long userId,
        Domain.Entities.TradingPair tradingPair,
        string side,
        string type,
        decimal price,
        decimal quantity)
    {
        var assetSymbol = side == "buy" 
            ? tradingPair.QuoteAsset 
            : tradingPair.BaseAsset;

        var requiredAmount = side == "buy" 
            ? price * quantity 
            : quantity;

        var asset = await _dbContext.Assets
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Symbol == assetSymbol);

        if (asset == null)
        {
            return (false, $"Asset {assetSymbol} not found");
        }

        if (asset.Available < requiredAmount)
        {
            return (false, "Insufficient balance");
        }

        // 冻结资产
        asset.Available -= requiredAmount;
        asset.Frozen += requiredAmount;
        asset.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _dbContext.SaveChangesAsync();

        return (true, string.Empty);
    }

    private long GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}

/// <summary>
/// 下单请求 DTO
/// </summary>
public class PlaceOrderRequest
{
    public required string Symbol { get; set; }
    public required string Side { get; set; } // "buy" or "sell"
    public required string Type { get; set; } // "limit" or "market"
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
}
