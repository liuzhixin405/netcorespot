using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Mapping;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Repositories; // 新增仓储接口引用
using CryptoSpot.Domain.Entities; // 引入领域实体
using CryptoSpot.Application.Abstractions.Services.Trading; // 交易对服务

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// K线数据服务实现
    /// </summary>
    public class KLineDataService : IKLineDataService // 实现 DTO 接口
    {
        // 移除 IKLineDataDomainService 依赖，直接访问仓储
        private readonly IKLineDataRepository _klineRepository;
        private readonly ITradingPairService _tradingPairService;
        private readonly IDtoMappingService _mappingService;
        private readonly ILogger<KLineDataService> _logger;

        public KLineDataService(
            IKLineDataRepository klineRepository,
            ITradingPairService tradingPairService,
            IDtoMappingService mappingService,
            ILogger<KLineDataService> logger)
        {
            _klineRepository = klineRepository;
            _tradingPairService = tradingPairService;
            _mappingService = mappingService;
            _logger = logger;
        }

        // =============== DTO 层方法 ===============
        public async Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
        {
            try
            {
                var klineData = await _klineRepository.GetKLineDataAsync(symbol, interval, limit);
                var dtoList = _mappingService.MapToDto(klineData, symbol);
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting K-line data for {Symbol} {Interval}", symbol, interval);
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError("获取K线数据失败");
            }
        }        public async Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(string symbol, string interval, long? startTime, long? endTime, int limit = 100)
        {
            try
            {
                IEnumerable<KLineData> klineData;
                if (startTime == null || endTime == null)
                {
                    klineData = await _klineRepository.GetKLineDataAsync(symbol, interval, limit);
                }
                else
                {
                    var start = DateTimeOffset.FromUnixTimeMilliseconds(startTime.Value).UtcDateTime;
                    var end = DateTimeOffset.FromUnixTimeMilliseconds(endTime.Value).UtcDateTime;
                    var tpIdResp = await _tradingPairService.GetTradingPairIdAsync(symbol);
                    var tpId = tpIdResp.Success ? tpIdResp.Data : 0;
                    if (tpId <= 0) klineData = Enumerable.Empty<KLineData>();
                    else klineData = await _klineRepository.GetKLineDataByTimeRangeAsync(tpId, interval, start, end);
                }
                var dtoList = _mappingService.MapToDto(klineData, symbol);
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting K-line data with time range for {Symbol} {Interval}", symbol, interval);
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError("获取K线数据失败");
            }
        }        public async Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetHistoricalKLineDataAsync(string symbol, string interval, long startTime, long endTime)
        {
            try
            {
                var start = DateTimeOffset.FromUnixTimeMilliseconds(startTime).UtcDateTime;
                var end = DateTimeOffset.FromUnixTimeMilliseconds(endTime).UtcDateTime;
                var tpIdResp = await _tradingPairService.GetTradingPairIdAsync(symbol);
                var tpId = tpIdResp.Success ? tpIdResp.Data : 0;
                IEnumerable<KLineData> klineData = tpId <= 0 ? 
                    Enumerable.Empty<KLineData>() : 
                    await _klineRepository.GetKLineDataByTimeRangeAsync(tpId, interval, start, end);
                var dtoList = _mappingService.MapToDto(klineData, symbol);
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateSuccess(dtoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting historical K-line data for {Symbol} {Interval}", symbol, interval);
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError("获取历史K线数据失败");
            }
        }        public async Task<ApiResponseDto<KLineDataDto?>> GetLatestKLineDataAsync(string symbol, string interval)
        {
            try
            {
                var tpIdResp = await _tradingPairService.GetTradingPairIdAsync(symbol);
                var tpId = tpIdResp.Success ? tpIdResp.Data : 0;
                KLineData? latestData = tpId <= 0 ? null : await _klineRepository.GetLatestKLineDataAsync(tpId, interval);
                var dto = latestData != null ? _mappingService.MapToDto(latestData, symbol) : null;
                return ApiResponseDto<KLineDataDto?>.CreateSuccess(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest K-line data for {Symbol} {Interval}", symbol, interval);
                return ApiResponseDto<KLineDataDto?>.CreateError("获取最新K线数据失败");
            }
        }

        public async Task<ApiResponseDto<IEnumerable<KLineDataDto>>> BatchGetKLineDataAsync(IEnumerable<string> symbols, string interval, int limit = 100)
        {
            try
            {
                var allKLineData = new List<KLineDataDto>();
                foreach (var symbol in symbols)
                {
                    var klineData = await _klineRepository.GetKLineDataAsync(symbol, interval, limit);
                    var dtoList = _mappingService.MapToDto(klineData, symbol);
                    allKLineData.AddRange(dtoList);
                }
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateSuccess(allKLineData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batch K-line data for interval {Interval}", interval);
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError("批量获取K线数据失败");
            }
        }

        public async Task<ApiResponseDto<KLineDataStatisticsDto>> GetKLineDataStatisticsAsync(string symbol, string interval)
        {
            try
            {
                var klineData = await _klineRepository.GetKLineDataAsync(symbol, interval, 10000); // 获取大量数据来计算统计
                var dataList = klineData.ToList();
                if (!dataList.Any())
                {
                    return ApiResponseDto<KLineDataStatisticsDto>.CreateSuccess(new KLineDataStatisticsDto());
                }
                var stats = new KLineDataStatisticsDto
                {
                    TotalRecords = dataList.Count,
                    FirstRecordTime = dataList.Min(k => k.OpenDateTime),
                    LastRecordTime = dataList.Max(k => k.CloseDateTime),
                    HighestPrice = dataList.Max(k => k.High),
                    LowestPrice = dataList.Min(k => k.Low),
                    TotalVolume = dataList.Sum(k => k.Volume)
                };
                return ApiResponseDto<KLineDataStatisticsDto>.CreateSuccess(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting K-line statistics for {Symbol} {Interval}", symbol, interval);
                return ApiResponseDto<KLineDataStatisticsDto>.CreateError("获取K线统计信息失败");
            }
        }

        public Task<ApiResponseDto<IEnumerable<string>>> GetSupportedSymbolsAsync()
        {
            try
            {
                var symbols = new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT", "ADAUSDT" };
                return Task.FromResult(ApiResponseDto<IEnumerable<string>>.CreateSuccess(symbols.AsEnumerable()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting supported symbols");
                return Task.FromResult(ApiResponseDto<IEnumerable<string>>.CreateError("获取支持的交易对失败"));
            }
        }

        public Task<ApiResponseDto<IEnumerable<string>>> GetSupportedIntervalsAsync()
        {
            try
            {
                var intervals = new[] { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w", "1M" };
                return Task.FromResult(ApiResponseDto<IEnumerable<string>>.CreateSuccess(intervals.AsEnumerable()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting supported intervals");
                return Task.FromResult(ApiResponseDto<IEnumerable<string>>.CreateError("获取支持的时间间隔失败"));
            }
        }

        public Task<ApiResponseDto<bool>> SubscribeKLineDataAsync(string symbol, string interval)
        {
            try
            {
                _logger.LogWarning("SubscribeKLineDataAsync not implemented for {Symbol} {Interval}", symbol, interval);
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "此功能尚未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to K-line data for {Symbol} {Interval}", symbol, interval);
                return Task.FromResult(ApiResponseDto<bool>.CreateError("订阅K线数据失败"));
            }
        }

        public Task<ApiResponseDto<bool>> UnsubscribeKLineDataAsync(string symbol, string interval)
        {
            try
            {
                _logger.LogWarning("UnsubscribeKLineDataAsync not implemented for {Symbol} {Interval}", symbol, interval);
                return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(false, "此功能尚未实现"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from K-line data for {Symbol} {Interval}", symbol, interval);
                return Task.FromResult(ApiResponseDto<bool>.CreateError("取消订阅K线数据失败"));
            }        }
    }
}
