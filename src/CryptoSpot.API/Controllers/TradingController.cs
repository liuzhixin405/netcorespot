using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CryptoSpot.Application.Abstractions.Trading; // migrated from Core.Interfaces.Trading
using CryptoSpot.Application.Abstractions.MarketData; // migrated from Core.Interfaces.MarketData
using CryptoSpot.Domain.Entities;
using System.Security.Claims;

namespace CryptoSpot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TradingController : ControllerBase
    {
        private readonly ITradingService _tradingService;
        private readonly IKLineDataService _klineDataService;
        private readonly IPriceDataService _priceDataService;
        private readonly ILogger<TradingController> _logger;

        public TradingController(
            ITradingService tradingService, 
            IKLineDataService klineDataService,
            IPriceDataService priceDataService,
            ILogger<TradingController> logger)
        {
            _tradingService = tradingService;
            _klineDataService = klineDataService;
            _priceDataService = priceDataService;
            _logger = logger;
        }

        [HttpGet("pairs")]
        public async Task<IActionResult> GetTradingPairs()
        {
            try
            {
                var pairs = await _tradingService.GetTradingPairsAsync();
                return Ok(new
                {
                    success = true,
                    data = pairs.Select(p => new
                    {
                        symbol = p.Symbol,
                        baseAsset = p.BaseAsset,
                        quoteAsset = p.QuoteAsset,
                        price = p.Price,
                        change24h = p.Change24h,
                        volume24h = p.Volume24h,
                        high24h = p.High24h,
                        low24h = p.Low24h,
                        lastUpdated = p.LastUpdated
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trading pairs");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        [HttpGet("pairs/{symbol}")]
        public async Task<IActionResult> GetTradingPair(string symbol)
        {
            try
            {
                var pair = await _tradingService.GetTradingPairAsync(symbol);
                if (pair == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Trading pair not found"
                    });
                }

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        symbol = pair.Symbol,
                        baseAsset = pair.BaseAsset,
                        quoteAsset = pair.QuoteAsset,
                        price = pair.Price,
                        change24h = pair.Change24h,
                        volume24h = pair.Volume24h,
                        high24h = pair.High24h,
                        low24h = pair.Low24h,
                        lastUpdated = pair.LastUpdated
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trading pair {Symbol}", symbol);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        [HttpGet("klines/{symbol}")]
        public async Task<IActionResult> GetKLineData(string symbol, [FromQuery] string interval = "1h", [FromQuery] int limit = 100)
        {
            try
            {
                // 从数据库获取K线数据（后台服务会自动同步数据）
                var klineData = await _klineDataService.GetKLineDataAsync(symbol, interval, limit);
                
                if (!klineData.Any())
                {
                    _logger.LogWarning("No K-line data found for {Symbol} {Interval}. Background service may still be syncing.", symbol, interval);
                    return NotFound(new
                    {
                        success = false,
                        message = "K-line data not available yet. Please try again later."
                    });
                }
                
                return Ok(new
                {
                    success = true,
                    data = klineData.Select(k => new
                    {
                        timestamp = k.OpenTime,
                        open = k.Open,
                        high = k.High,
                        low = k.Low,
                        close = k.Close,
                        volume = k.Volume
                    }).OrderBy(k => k.timestamp)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting K-line data for {Symbol} {Interval}", symbol, interval);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }


        [HttpGet("assets")]
        public async Task<IActionResult> GetUserAssets()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Unauthorized"
                    });
                }

                var assets = await _tradingService.GetUserAssetsAsync(userId.Value);
                return Ok(new
                {
                    success = true,
                    data = assets.Select(a => new
                    {
                        symbol = a.Symbol,
                        available = a.Available,
                        frozen = a.Frozen,
                        total = a.Total
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user assets");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        [HttpGet("orders")]
        public async Task<IActionResult> GetUserOrders([FromQuery] string? symbol = null)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Unauthorized"
                    });
                }

                var orders = await _tradingService.GetUserOrdersAsync(userId.Value, symbol);
                return Ok(new
                {
                    success = true,
                    data = orders.Select(o => new
                    {
                        id = o.Id,
                        orderId = o.OrderId,
                        symbol = o.TradingPair.Symbol,
                        side = o.Side.ToString(),
                        type = o.Type.ToString(),
                        quantity = o.Quantity,
                        price = o.Price,
                        filledQuantity = o.FilledQuantity,
                        remainingQuantity = o.RemainingQuantity,
                        status = o.Status.ToString(),
                        createdAt = o.CreatedAt,
                        updatedAt = o.UpdatedAt
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user orders");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        [HttpGet("open-orders")]
        public async Task<IActionResult> GetOpenOrders([FromQuery] string? symbol = null)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Unauthorized"
                    });
                }

                var orders = await _tradingService.GetUserOrdersAsync(userId.Value, symbol);
                var openStatuses = new[] { OrderStatus.Pending, OrderStatus.Active, OrderStatus.PartiallyFilled };
                var openOrders = orders.Where(o => openStatuses.Contains(o.Status));

                return Ok(new
                {
                    success = true,
                    data = openOrders.Select(o => new
                    {
                        id = o.Id,
                        orderId = o.OrderId,
                        symbol = o.TradingPair.Symbol,
                        side = o.Side.ToString(),
                        type = o.Type.ToString(),
                        quantity = o.Quantity,
                        price = o.Price,
                        filledQuantity = o.FilledQuantity,
                        remainingQuantity = o.RemainingQuantity,
                        averagePrice = o.AveragePrice,
                        status = o.Status.ToString(),
                        createdAt = o.CreatedAt,
                        updatedAt = o.UpdatedAt
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting open orders");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        [HttpGet("order-history")]
        public async Task<IActionResult> GetOrderHistory([FromQuery] string? symbol = null)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized(new { success = false, message = "Unauthorized" });
                }
                var orders = await _tradingService.GetUserOrdersAsync(userId.Value, symbol);
                var historyStatuses = new[] { OrderStatus.Filled, OrderStatus.Cancelled };
                var historyOrders = orders.Where(o => historyStatuses.Contains(o.Status));
                return Ok(new
                {
                    success = true,
                    data = historyOrders.Select(o => new
                    {
                        id = o.Id,
                        orderId = o.OrderId,
                        symbol = o.TradingPair.Symbol,
                        side = o.Side.ToString(),
                        type = o.Type.ToString(),
                        quantity = o.Quantity,
                        price = o.Price,
                        filledQuantity = o.FilledQuantity,
                        remainingQuantity = o.RemainingQuantity,
                        averagePrice = o.AveragePrice,
                        status = o.Status.ToString(),
                        createdAt = o.CreatedAt,
                        updatedAt = o.UpdatedAt
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting order history");
                return StatusCode(500, new { success = false, message = "Internal server error" });
            }
        }

        [HttpGet("trades")]
        public async Task<IActionResult> GetUserTrades([FromQuery] string? symbol = null)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Unauthorized"
                    });
                }

                var trades = await _tradingService.GetUserTradesAsync(userId.Value, symbol);
                return Ok(new
                {
                    success = true,
                    data = trades.Select(t => new
                    {
                        id = t.Id,
                        tradeId = t.TradeId,
                        symbol = t.TradingPair.Symbol,
                        quantity = t.Quantity,
                        price = t.Price,
                        fee = t.Fee,
                        feeAsset = t.FeeAsset,
                        totalValue = t.TotalValue,
                        executedAt = t.ExecutedAt
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user trades");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        [HttpPost("orders")]
        public async Task<IActionResult> SubmitOrder([FromBody] SubmitOrderRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Unauthorized"
                    });
                }

                var order = await _tradingService.SubmitOrderAsync(userId.Value, request);
                if (order == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Failed to submit order"
                    });
                }

                // 确保导航属性可用（可能未跟踪加载）
                var symbolValue = order.TradingPair?.Symbol ?? request.Symbol;
                var priceValue = order.Price;
                if (priceValue == null && order.Type == OrderType.Market)
                {
                    // 市价单：使用当前交易对最新价格（如果有）
                    try
                    {
                        var pair = await _tradingService.GetTradingPairAsync(symbolValue);
                        if (pair != null && pair.Price > 0)
                        {
                            priceValue = pair.Price;
                        }
                    }
                    catch { }
                }

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        id = order.Id,
                        orderId = order.OrderId,
                        symbol = symbolValue,
                        side = order.Side.ToString(),
                        type = order.Type.ToString(),
                        quantity = order.Quantity,
                        price = priceValue,
                        filledQuantity = order.FilledQuantity,
                        remainingQuantity = order.RemainingQuantity,
                        averagePrice = order.AveragePrice,
                        status = order.Status.ToString(),
                        createdAt = order.CreatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting order");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        [HttpDelete("orders/{orderId}")]
        public async Task<IActionResult> CancelOrder(int orderId)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Unauthorized"
                    });
                }

                var success = await _tradingService.CancelOrderAsync(userId.Value, orderId);
                if (!success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Failed to cancel order"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "Order cancelled successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling order {OrderId}", orderId);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        private int? GetCurrentUserId()
        {
            // 尝试多种可能的 claim 类型
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? 
                             User.FindFirst("nameid") ?? 
                             User.FindFirst("sub");
            
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }
            
            return null;
        }
    }
}
