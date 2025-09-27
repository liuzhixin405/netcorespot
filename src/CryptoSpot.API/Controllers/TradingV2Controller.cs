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
    /// 交易控制器V2 - 使用DTO架构
    /// </summary>
    [ApiController]
    [Route("api/v2/[controller]")]
    [Authorize]
    public class TradingV2Controller : ControllerBase
    {
        private readonly ITradingServiceV2 _tradingService;
        private readonly ILogger<TradingV2Controller> _logger;

        public TradingV2Controller(
            ITradingServiceV2 tradingService,
            ILogger<TradingV2Controller> logger)
        {
            _tradingService = tradingService;
            _logger = logger;
        }

        /// <summary>
        /// 获取当前用户ID
        /// </summary>
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                throw new UnauthorizedAccessException("用户未认证");
            }
            return userId;
        }

        #region 交易对相关

        /// <summary>
        /// 获取所有交易对
        /// </summary>
        /// <returns>交易对列表</returns>
        [HttpGet("pairs")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<TradingPairDto>>>> GetTradingPairs()
        {
            var result = await _tradingService.GetTradingPairsAsync();
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 获取指定交易对详情
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <returns>交易对详情</returns>
        [HttpGet("pairs/{symbol}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<TradingPairDto?>>> GetTradingPair(string symbol)
        {
            var result = await _tradingService.GetTradingPairAsync(symbol);
            return result.Success ? Ok(result) : NotFound(result);
        }

        /// <summary>
        /// 获取交易对摘要列表
        /// </summary>
        /// <returns>交易对摘要列表</returns>
        [HttpGet("pairs/summary")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<TradingPairSummaryDto>>>> GetTradingPairSummaries()
        {
            var result = await _tradingService.GetTradingPairSummariesAsync();
            return result.Success ? Ok(result) : BadRequest(result);
        }

        #endregion

        #region K线数据相关

        /// <summary>
        /// 获取K线数据
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <param name="timeFrame">时间框架</param>
        /// <param name="limit">数量限制</param>
        /// <returns>K线数据列表</returns>
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

        /// <summary>
        /// 获取最新K线数据
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <param name="timeFrame">时间框架</param>
        /// <returns>最新K线数据</returns>
        [HttpGet("klines/{symbol}/latest")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<KLineDataDto?>>> GetLatestKLineData(
            string symbol, 
            [FromQuery] string timeFrame = "1h")
        {
            var result = await _tradingService.GetLatestKLineDataAsync(symbol, timeFrame);
            return result.Success ? Ok(result) : NotFound(result);
        }

        #endregion

        #region 用户资产相关

        /// <summary>
        /// 获取当前用户资产
        /// </summary>
        /// <returns>用户资产列表</returns>
        [HttpGet("assets")]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<AssetDto>>>> GetUserAssets()
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetUserAssetsAsync(userId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 获取当前用户资产汇总
        /// </summary>
        /// <returns>资产汇总</returns>
        [HttpGet("assets/summary")]
        public async Task<ActionResult<ApiResponseDto<AssetSummaryDto>>> GetUserAssetSummary()
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetUserAssetSummaryAsync(userId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        #endregion

        #region 订单相关

        /// <summary>
        /// 获取当前用户订单
        /// </summary>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <returns>订单列表</returns>
        [HttpGet("orders")]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<OrderDto>>>> GetUserOrders([FromQuery] string? symbol = null)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetUserOrdersAsync(userId, symbol);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 获取当前用户活跃订单
        /// </summary>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <returns>活跃订单列表</returns>
        [HttpGet("orders/open")]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<OrderDto>>>> GetOpenOrders([FromQuery] string? symbol = null)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetOpenOrdersAsync(userId, symbol);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 获取指定订单详情
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <returns>订单详情</returns>
        [HttpGet("orders/{orderId}")]
        public async Task<ActionResult<ApiResponseDto<OrderDto?>>> GetOrder(int orderId)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetOrderAsync(userId, orderId);
            return result.Success ? Ok(result) : NotFound(result);
        }

        /// <summary>
        /// 提交订单
        /// </summary>
        /// <param name="request">订单请求</param>
        /// <returns>创建的订单</returns>
        [HttpPost("orders")]
        public async Task<ActionResult<ApiResponseDto<OrderDto?>>> SubmitOrder([FromBody] CreateOrderRequestDto request)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.SubmitOrderAsync(userId, request);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 取消订单
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <returns>操作结果</returns>
        [HttpDelete("orders/{orderId}")]
        public async Task<ActionResult<ApiResponseDto<bool>>> CancelOrder(int orderId)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.CancelOrderAsync(userId, orderId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 批量取消订单
        /// </summary>
        /// <param name="request">批量取消请求（可选）</param>
        /// <returns>批量取消结果</returns>
        [HttpDelete("orders/batch")]
        public async Task<ActionResult<ApiResponseDto<BatchCancelOrdersResultDto>>> CancelAllOrders([FromBody] BatchCancelOrdersRequestDto? request = null)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.CancelAllOrdersAsync(userId, request);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 测试订单
        /// </summary>
        /// <param name="request">订单请求</param>
        /// <returns>测试结果</returns>
        [HttpPost("orders/test")]
        public async Task<ActionResult<ApiResponseDto<TestOrderResultDto>>> TestOrder([FromBody] CreateOrderRequestDto request)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.TestOrderAsync(userId, request);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        #endregion

        #region 交易记录相关

        /// <summary>
        /// 获取当前用户交易记录
        /// </summary>
        /// <param name="symbol">交易对符号（可选）</param>
        /// <returns>交易记录列表</returns>
        [HttpGet("trades")]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<TradeDto>>>> GetUserTrades([FromQuery] string? symbol = null)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetUserTradesAsync(userId, symbol);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 获取指定订单的交易记录
        /// </summary>
        /// <param name="orderId">订单ID</param>
        /// <returns>交易记录列表</returns>
        [HttpGet("orders/{orderId}/trades")]
        public async Task<ActionResult<ApiResponseDto<IEnumerable<TradeDto>>>> GetOrderTrades(int orderId)
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetOrderTradesAsync(userId, orderId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// 获取当前用户交易统计
        /// </summary>
        /// <returns>交易统计</returns>
        [HttpGet("trades/statistics")]
        public async Task<ActionResult<ApiResponseDto<TradeStatisticsDto>>> GetUserTradeStatistics()
        {
            var userId = GetCurrentUserId();
            var result = await _tradingService.GetUserTradeStatisticsAsync(userId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        #endregion

        #region 订单簿相关

        /// <summary>
        /// 获取订单簿深度
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <param name="depth">深度</param>
        /// <returns>订单簿深度</returns>
        [HttpGet("orderbook/{symbol}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<OrderBookDepthDto>>> GetOrderBookDepth(
            string symbol, 
            [FromQuery] int depth = 20)
        {
            var result = await _tradingService.GetOrderBookDepthAsync(symbol, depth);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        #endregion
    }
}
