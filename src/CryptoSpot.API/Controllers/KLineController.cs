using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CryptoSpot.Application.Abstractions.Services.MarketData;

namespace CryptoSpot.API.Controllers
{
    /// <summary>
    /// K线数据控制器 - 提供历史K线数据API
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous] // K线数据允许匿名访问
    public class KLineController : ControllerBase
    {
        private readonly IKLineDataService _klineDataService;
        private readonly ILogger<KLineController> _logger;

        public KLineController(
            IKLineDataService klineDataService,
            ILogger<KLineController> logger)
        {
            _klineDataService = klineDataService;
            _logger = logger;
        }

        /// <summary>
        /// 获取K线历史数据
        /// </summary>
        /// <param name="symbol">交易对符号，如 BTCUSDT</param>
        /// <param name="interval">时间间隔，如 1m, 5m, 15m, 1h, 4h, 1d</param>
        /// <param name="startTime">开始时间戳（毫秒）</param>
        /// <param name="endTime">结束时间戳（毫秒）</param>
        /// <param name="limit">返回数据条数，默认1000，最大1000</param>
        /// <returns>K线数据列表</returns>
        [HttpGet("history")]
        public async Task<IActionResult> GetKLineHistory(
            [FromQuery] string symbol,
            [FromQuery] string interval,
            [FromQuery] long? startTime = null,
            [FromQuery] long? endTime = null,
            [FromQuery] int limit = 1000)
        {
            try
            {
                // 参数验证
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    return BadRequest(new { error = "交易对符号不能为空" });
                }

                if (string.IsNullOrWhiteSpace(interval))
                {
                    return BadRequest(new { error = "时间间隔不能为空" });
                }

                // 限制返回条数
                limit = Math.Min(limit, 1000);

                // 获取K线数据
                var klineData = await _klineDataService.GetKLineDataAsync(symbol, interval, startTime, endTime, limit);

                // 转换为前端期望的格式
                var response = klineData.Data.Select(k => new
                {
                    timestamp = k.OpenTime,
                    open = k.Open,
                    high = k.High,
                    low = k.Low,
                    close = k.Close,
                    volume = k.Volume,
                    symbol = symbol,
                    interval = interval
                }).ToList();

                return Ok(new
                {
                    success = true,
                    data = response,
                    symbol = symbol,
                    interval = interval,
                    count = response.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取K线历史数据失败: Symbol={Symbol}, Interval={Interval}", symbol, interval);
                return StatusCode(500, new { error = "获取K线数据失败" });
            }
        }

        /// <summary>
        /// 获取最新的K线数据
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <param name="interval">时间间隔</param>
        /// <returns>最新的K线数据</returns>
        [HttpGet("latest")]
        public async Task<IActionResult> GetLatestKLine(
            [FromQuery] string symbol,
            [FromQuery] string interval)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(interval))
                {
                    return BadRequest(new { error = "交易对符号和时间间隔不能为空" });
                }

                // 获取最新的一条K线数据
                var klineData = await _klineDataService.GetKLineDataAsync(symbol, interval, null, null, 1);
                var latestKline = klineData.Data.FirstOrDefault();

                if (latestKline == null)
                {
                    return NotFound(new { error = "未找到K线数据" });
                }

                var response = new
                {
                    timestamp = latestKline.OpenTime,
                    open = latestKline.Open,
                    high = latestKline.High,
                    low = latestKline.Low,
                    close = latestKline.Close,
                    volume = latestKline.Volume,
                    symbol = symbol,
                    interval = interval
                };

                return Ok(new
                {
                    success = true,
                    data = response
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最新K线数据失败: Symbol={Symbol}, Interval={Interval}", symbol, interval);
                return StatusCode(500, new { error = "获取最新K线数据失败" });
            }
        }

        /// <summary>
        /// 获取支持的交易对列表
        /// </summary>
        /// <returns>支持的交易对列表</returns>
        [HttpGet("symbols")]
        public IActionResult GetSupportedSymbols()
        {
            var symbols = new[]
            {
                "BTCUSDT",
                "ETHUSDT", 
                "SOLUSDT",
                "ADAUSDT",
                "DOTUSDT",
                "LINKUSDT",
                "UNIUSDT",
                "LTCUSDT",
                "BCHUSDT",
                "XLMUSDT"
            };

            return Ok(new
            {
                success = true,
                data = symbols
            });
        }

        /// <summary>
        /// 获取支持的时间间隔
        /// </summary>
        /// <returns>支持的时间间隔列表</returns>
        [HttpGet("intervals")]
        public IActionResult GetSupportedIntervals()
        {
            var intervals = new[]
            {
                "1m", "3m", "5m", "15m", "30m",
                "1h", "2h", "4h", "6h", "8h", "12h",
                "1d", "3d", "1w", "1M"
            };

            return Ok(new
            {
                success = true,
                data = intervals
            });
        }
    }
}
