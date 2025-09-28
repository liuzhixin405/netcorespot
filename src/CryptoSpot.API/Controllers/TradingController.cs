using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Users;
using CryptoSpot.Application.Abstractions.Services.Trading;

namespace CryptoSpot.API.Controllers
{
    /// <summary>
    /// 交易控制器
    /// </summary>
    [ApiController]
    [Route("api/trading")]
    [Authorize]
    public class TradingController : ControllerBase
    {
        private readonly ITradingService _tradingService;
        private readonly ILogger<TradingController> _logger;

        public TradingController(
            ITradingService tradingService,
            ILogger<TradingController> logger)
        {
            _tradingService = tradingService;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                throw new UnauthorizedAccessException("用户未认证");
            }
            return userId;
        }

        // 交易对相关
        [HttpGet("pairs")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<TradingPairDto>>>> GetTradingPairs()
        {
            var result = await _tradingService.GetTradingPairsAsync();
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("pairs/{symbol}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<TradingPairDto?>>> GetTradingPair(string symbol)
        {
            var result = await _tradingService.GetTradingPairAsync(symbol);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpGet("pairs/summary")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<TradingPairSummaryDto>>>> GetTradingPairSummaries()
        {
            var result = await _tradingService.GetTradingPairSummariesAsync();
            return result.Success ? Ok(result) : BadRequest(result);
        }

        // K线数据
        [HttpGet("klines/{symbol}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<KLineDataDto>>>> GetKLineData(
            string symbol,
            [FromQuery] string timeFrame = "1h",
            [FromQuery] int limit = 100)
        {
            var result = await _tradingService.GetKLineDataAsync(symbol, timeFrame, limit);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("klines/{symbol}/latest")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<KLineDataDto?>>> GetLatestKLineData(
            string symbol,
            [FromQuery] string timeFrame = "1h")
        {
            var result = await _tradingService.GetLatestKLineDataAsync(symbol, timeFrame);
            return result.Success ? Ok(result) : NotFound(result);
        }

        // 用户资产
        [HttpGet("assets")]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<AssetDto>>>> GetUserAssets()
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetUserAssetsAsync(userId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("assets/summary")]
        public async Task<ActionResult<ApiResponseDto<AssetSummaryDto>>> GetUserAssetSummary()
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetUserAssetSummaryAsync(userId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        // 订单
        [HttpGet("orders")]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<OrderDto>>>> GetUserOrders([FromQuery] string? symbol = null)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetUserOrdersAsync(userId, symbol);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("orders/open")]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<OrderDto>>>> GetOpenOrders([FromQuery] string? symbol = null)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetOpenOrdersAsync(userId, symbol);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("orders/{orderId}")]
        public async Task<ActionResult<ApiResponseDto<OrderDto?>>> GetOrder(int orderId)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetOrderAsync(userId, orderId);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost("orders")]
        public async Task<ActionResult<ApiResponseDto<OrderDto?>>> SubmitOrder([FromBody] CreateOrderRequestDto request)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.SubmitOrderAsync(userId, request);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpDelete("orders/{orderId}")]
        public async Task<ActionResult<ApiResponseDto<bool>>> CancelOrder(int orderId)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.CancelOrderAsync(userId, orderId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpDelete("orders/batch")]
        public async Task<ActionResult<ApiResponseDto<BatchCancelOrdersResultDto>>> CancelAllOrders([FromBody] BatchCancelOrdersRequestDto? request = null)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.CancelAllOrdersAsync(userId, request);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPost("orders/test")]
        public async Task<ActionResult<ApiResponseDto<TestOrderResultDto>>> TestOrder([FromBody] CreateOrderRequestDto request)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.TestOrderAsync(userId, request);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        // 交易记录
        [HttpGet("trades")]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<TradeDto>>>> GetUserTrades([FromQuery] string? symbol = null)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetUserTradesAsync(userId, symbol);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("orders/{orderId}/trades")]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<TradeDto>>>> GetOrderTrades(int orderId)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetOrderTradesAsync(userId, orderId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("trades/statistics")]
        public async Task<ActionResult<ApiResponseDto<TradeStatisticsDto>>> GetUserTradeStatistics()
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetUserTradeStatisticsAsync(userId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        // 订单簿
        [HttpGet("orderbook/{symbol}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<OrderBookDepthDto>>> GetOrderBookDepth(string symbol, [FromQuery] int depth = 20)
        {
            var result = await _tradingService.GetOrderBookDepthAsync(symbol, depth);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}