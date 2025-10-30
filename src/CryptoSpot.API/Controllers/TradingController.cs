using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using CryptoSpot.Application.DTOs.MarketData;
using System.Text.Json;
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
            if (!ModelState.IsValid)
            {
                var validationErrors = ModelState.Where(kv => kv.Value != null && kv.Value.Errors != null && kv.Value.Errors.Count > 0)
                    .ToDictionary(kv => kv.Key ?? string.Empty, kv => kv.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? (e.Exception?.Message ?? string.Empty) : e.ErrorMessage).Where(s => !string.IsNullOrEmpty(s)).ToArray());

                var op = CryptoSpot.Application.DTOs.Common.OperationResultDto.CreateValidationFailure(validationErrors);
                // 将结构化验证错误信息放在 ApiResponseDto 的 Error 字段并返回 400，便于前端解析
                var apiResp = ApiResponseDto<OrderDto?>.CreateError(op.Message ?? "请求参数验证失败");
                apiResp.Error = op.ValidationErrors != null && op.ValidationErrors.Any() ? string.Join("; ", op.ValidationErrors.Select(kv => $"{kv.Key}:{string.Join(',', kv.Value)}")) : op.Message;
                // 记录详细日志，包含序列化的请求体与字段级错误，便于排查
                try
                {
                    var reqJson = JsonSerializer.Serialize(request);
                    _logger.LogWarning("SubmitOrder ModelState invalid. Request: {RequestJson}, ValidationErrors: {ValidationErrors}", reqJson, apiResp.Error);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SubmitOrder ModelState invalid but failed to serialize request for logging. Errors: {Errors}", apiResp.Error);
                }
                return BadRequest(apiResp);
            }
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

        // 市场最近成交 (公开数据)
        [HttpGet("market/trades/{symbol}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<MarketTradeDto>>>> GetMarketRecentTrades(
            string symbol, 
            [FromQuery] int limit = 50)
        {
            var result = await _tradingService.GetMarketRecentTradesAsync(symbol, limit);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}