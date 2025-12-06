using CryptoSpot.MatchEngine.Core;
using CryptoSpot.MatchEngine.Services;
using Microsoft.AspNetCore.Mvc;

namespace CryptoSpot.MatchEngine.Controllers;

/// <summary>
/// 撮合引擎 API 控制器
/// </summary>
[ApiController]
[Route("api/match")]
public class MatchEngineController : ControllerBase
{
    private readonly InMemoryMatchEngineService _matchEngine;
    private readonly InMemoryAssetStore _assetStore;
    private readonly ILogger<MatchEngineController> _logger;

    public MatchEngineController(
        InMemoryMatchEngineService matchEngine,
        InMemoryAssetStore assetStore,
        ILogger<MatchEngineController> logger)
    {
        _matchEngine = matchEngine;
        _assetStore = assetStore;
        _logger = logger;
    }

    /// <summary>
    /// 提交订单到撮合引擎
    /// </summary>
    [HttpPost("orders")]
    public async Task<IActionResult> SubmitOrder([FromBody] SubmitOrderRequest request)
    {
        try
        {
            var result = await _matchEngine.SubmitOrderAsync(
                request.UserId,
                request.Symbol,
                request.Side,
                request.Type,
                request.Price,
                request.Quantity
            );

            return Ok(new
            {
                success = true,
                data = new
                {
                    orderId = result.OrderId,
                    symbol = result.Symbol,
                    status = result.Status.ToString(),
                    executedQuantity = result.ExecutedQuantity,
                    trades = result.Trades?.Select(t => new
                    {
                        tradeId = t.Id,
                        price = t.Price,
                        quantity = t.Quantity,
                        buyOrderId = t.BuyOrderId,
                        sellOrderId = t.SellOrderId,
                        timestamp = t.Timestamp
                    })
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "提交订单失败: {Request}", request);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 取消订单
    /// </summary>
    [HttpDelete("orders/{orderId}")]
    public async Task<IActionResult> CancelOrder(long orderId, [FromQuery] long userId)
    {
        try
        {
            var success = await _matchEngine.CancelOrderAsync(orderId, userId);
            return Ok(new { success, message = success ? "订单取消成功" : "订单取消失败" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消订单失败: OrderId={OrderId}, UserId={UserId}", orderId, userId);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 查询用户资产
    /// </summary>
    [HttpGet("assets/{userId}")]
    public IActionResult GetUserAssets(long userId)
    {
        try
        {
            var assets = _assetStore.GetUserAssets(userId);
            return Ok(new
            {
                success = true,
                data = assets.Select(a => new
                {
                    asset = a.Key,
                    available = a.Value.Available,
                    frozen = a.Value.Frozen,
                    total = a.Value.Available + a.Value.Frozen
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询用户资产失败: UserId={UserId}", userId);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 查询订单簿深度
    /// </summary>
    [HttpGet("orderbook/{symbol}")]
    public IActionResult GetOrderBook(string symbol, [FromQuery] int depth = 20)
    {
        try
        {
            var orderBook = _matchEngine.GetOrderBook(symbol, depth);
            return Ok(new
            {
                success = true,
                data = orderBook
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询订单簿失败: Symbol={Symbol}", symbol);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        return Ok(new
        {
            status = "healthy",
            service = "MatchEngine",
            timestamp = DateTime.UtcNow
        });
    }
}

/// <summary>
/// 提交订单请求
/// </summary>
public class SubmitOrderRequest
{
    public long UserId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty; // "Buy" or "Sell"
    public string Type { get; set; } = string.Empty; // "Limit" or "Market"
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
}
