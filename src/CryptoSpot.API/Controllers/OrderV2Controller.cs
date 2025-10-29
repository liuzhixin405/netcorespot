using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CryptoSpot.API.Controllers;

/// <summary>
/// Redis-First 订单 API（所有操作在 Redis 中）
/// </summary>
[ApiController]
[Route("api/v2/[controller]")]
[Authorize]
public class OrderController : ControllerBase
{
    private readonly RedisOrderRepository _redisOrders;
    private readonly RedisAssetRepository _redisAssets;
    private readonly ILogger<OrderController> _logger;

    public OrderController(
        RedisOrderRepository redisOrders,
        RedisAssetRepository redisAssets,
        ILogger<OrderController> logger)
    {
        _redisOrders = redisOrders;
        _redisAssets = redisAssets;
        _logger = logger;
    }

    #region 下单

    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();

            // 验证参数
            if (string.IsNullOrEmpty(request.Symbol))
            {
                return BadRequest("交易对不能为空");
            }

            if (request.Quantity <= 0)
            {
                return BadRequest("数量必须大于 0");
            }

            if (request.Type == "Limit" && request.Price <= 0)
            {
                return BadRequest("限价单价格必须大于 0");
            }

            // 创建订单
            var order = new Order
            {
                UserId = userId,
                OrderId = $"ORD_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000,9999)}",
                ClientOrderId = $"CL_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000,9999)}",
                Side = request.Side.ToLower() == "buy" ? OrderSide.Buy : OrderSide.Sell,
                Type = request.Type.ToLower() == "market" ? OrderType.Market : OrderType.Limit,
                Price = request.Price,
                Quantity = request.Quantity,
                FilledQuantity = 0,
                Status = OrderStatus.Pending
            };

            // Persist order to Redis - MatchEngine (separate process) will consume orders from Redis stream and perform matching
            var createdOrder = await _redisOrders.CreateOrderAsync(order, request.Symbol);

            return Ok(new
            {
                success = true,
                data = new
                {
                    orderId = createdOrder.Id,

                    side = createdOrder.Side.ToString(),
                    type = createdOrder.Type.ToString(),
                    price = createdOrder.Price,
                    quantity = createdOrder.Quantity,
                    filledQuantity = createdOrder.FilledQuantity,
                    status = createdOrder.Status.ToString(),
                    createdAt = createdOrder.CreatedAt
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "下单失败: UserId={UserId}", GetCurrentUserId());
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下单异常: UserId={UserId}", GetCurrentUserId());
            return StatusCode(500, new { success = false, message = "下单失败，请稍后重试" });
        }
    }

    #endregion

    #region 查询订单

    [HttpGet("my-orders")]
    public async Task<IActionResult> GetMyOrders([FromQuery] int limit = 100)
    {
        try
        {
            var userId = GetCurrentUserId();
            var orders = await _redisOrders.GetUserOrdersAsync(userId, limit);

            return Ok(new
            {
                success = true,
                data = orders.Select(o => new
                {
                    orderId = o.Id,

                    side = o.Side.ToString(),
                    type = o.Type.ToString(),
                    price = o.Price,
                    quantity = o.Quantity,
                    filledQuantity = o.FilledQuantity,
                    status = o.Status.ToString(),
                    createdAt = o.CreatedAt,
                    updatedAt = o.UpdatedAt
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询订单失败: UserId={UserId}", GetCurrentUserId());
            return StatusCode(500, new { success = false, message = "查询失败" });
        }
    }

    [HttpGet("{orderId}")]
    public async Task<IActionResult> GetOrder(int orderId)
    {
        try
        {
            var order = await _redisOrders.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                return NotFound(new { success = false, message = "订单不存在" });
            }

            var userId = GetCurrentUserId();
            if (order.UserId != userId)
            {
                return Forbid();
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    orderId = order.Id,
               
                    side = order.Side.ToString(),
                    type = order.Type.ToString(),
                    price = order.Price,
                    quantity = order.Quantity,
                    filledQuantity = order.FilledQuantity,
                    status = order.Status.ToString(),
                    createdAt = order.CreatedAt,
                    updatedAt = order.UpdatedAt
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询订单详情失败: OrderId={OrderId}", orderId);
            return StatusCode(500, new { success = false, message = "查询失败" });
        }
    }

    #endregion

    #region 取消订单

    [HttpDelete("{orderId}")]
    public async Task<IActionResult> CancelOrder(int orderId,string symbol)
    {
        try
        {
            var userId = GetCurrentUserId();
            var success = await _redisOrders.CancelOrderAsync(orderId, userId);

            if (!success)
            {
                return BadRequest(new { success = false, message = "订单无法取消" });
            }

            return Ok(new { success = true, message = "订单已取消" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消订单失败: OrderId={OrderId}", orderId);
            return StatusCode(500, new { success = false, message = "取消失败" });
        }
    }

    #endregion

    #region 资产查询

    [HttpGet("~/api/v2/assets")]
    public async Task<IActionResult> GetMyAssets()
    {
        try
        {
            var userId = GetCurrentUserId();
            var assets = await _redisAssets.GetUserAssetsAsync(userId);

            return Ok(new
            {
                success = true,
                data = assets.Select(a => new
                {
                    currency = a.Symbol,
                    availableBalance = a.Available,
                    frozenBalance = a.Frozen,
                    totalBalance = a.Available + a.Frozen
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询资产失败: UserId={UserId}", GetCurrentUserId());
            return StatusCode(500, new { success = false, message = "查询失败" });
        }
    }

    #endregion

    #region 辅助方法

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException("用户未登录");
        }
        return userId;
    }

    #endregion
}

/// <summary>
/// 下单请求
/// </summary>
public class PlaceOrderRequest
{
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = ""; // "Buy" 或 "Sell"
    public string Type { get; set; } = "Limit"; // "Limit" 或 "Market"
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
}
