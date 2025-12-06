using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.MarketData;
using CryptoSpot.Application.Mapping;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services;

/// <summary>
/// K线数据服务实现（简化版 - 纯数据库）
/// </summary>
public class KLineDataService : IKLineDataService
{
    private readonly IKLineDataRepository _repository;
    private readonly ITradingPairRepository _tradingPairRepository;
    private readonly IDtoMappingService _mapping;
    private readonly ILogger<KLineDataService> _logger;

    public KLineDataService(
        IKLineDataRepository repository,
        ITradingPairRepository tradingPairRepository,
        IDtoMappingService mapping,
        ILogger<KLineDataService> logger)
    {
        _repository = repository;
        _tradingPairRepository = tradingPairRepository;
        _mapping = mapping;
        _logger = logger;
    }
    
    private async Task<long?> GetTradingPairIdAsync(string symbol)
    {
        var pair = await _tradingPairRepository.GetBySymbolAsync(symbol);
        return pair?.Id;
    }

    public async Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(
        string symbol, 
        string interval, 
        int limit = 100)
    {
        try
        {
            var data = await _repository.GetKLineDataAsync(symbol, interval, limit);
            var dtos = _mapping.MapToDto(data, symbol).ToList();
            return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateSuccess(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取K线数据失败: {Symbol} {Interval}", symbol, interval);
            return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError("获取K线数据失败");
        }
    }

    public async Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetKLineDataAsync(
        string symbol, 
        string interval, 
        long? startTime, 
        long? endTime, 
        int limit = 100)
    {
        try
        {
            var tradingPairId = await GetTradingPairIdAsync(symbol);
            if (!tradingPairId.HasValue)
            {
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError($"交易对 {symbol} 不存在");
            }

            var startDateTime = startTime.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(startTime.Value).DateTime : DateTime.UtcNow.AddDays(-7);
            var endDateTime = endTime.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(endTime.Value).DateTime : DateTime.UtcNow;
            var data = await _repository.GetKLineDataByTimeRangeAsync(tradingPairId.Value, interval, startDateTime, endDateTime);
            var dtos = _mapping.MapToDto(data, symbol).ToList();
            return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateSuccess(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取K线数据失败: {Symbol} {Interval}", symbol, interval);
            return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError("获取K线数据失败");
        }
    }

    public async Task<ApiResponseDto<IEnumerable<KLineDataDto>>> GetHistoricalKLineDataAsync(
        string symbol, 
        string interval, 
        long startTime, 
        long endTime)
    {
        try
        {
            var tradingPairId = await GetTradingPairIdAsync(symbol);
            if (!tradingPairId.HasValue)
            {
                return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError($"交易对 {symbol} 不存在");
            }

            var startDateTime = DateTimeOffset.FromUnixTimeMilliseconds(startTime).DateTime;
            var endDateTime = DateTimeOffset.FromUnixTimeMilliseconds(endTime).DateTime;
            var data = await _repository.GetKLineDataByTimeRangeAsync(tradingPairId.Value, interval, startDateTime, endDateTime);
            var dtos = _mapping.MapToDto(data, symbol).ToList();
            return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateSuccess(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取历史K线数据失败: {Symbol} {Interval}", symbol, interval);
            return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError("获取历史K线数据失败");
        }
    }

    public async Task<ApiResponseDto<KLineDataDto?>> GetLatestKLineDataAsync(string symbol, string interval)
    {
        try
        {
            var tradingPairId = await GetTradingPairIdAsync(symbol);
            if (!tradingPairId.HasValue)
            {
                return ApiResponseDto<KLineDataDto?>.CreateError($"交易对 {symbol} 不存在");
            }

            var data = await _repository.GetLatestKLineDataAsync(tradingPairId.Value, interval);
            if (data == null)
            {
                return ApiResponseDto<KLineDataDto?>.CreateError("未找到K线数据");
            }

            var dto = _mapping.MapToDto(data, symbol);
            return ApiResponseDto<KLineDataDto?>.CreateSuccess(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取最新K线数据失败: {Symbol} {Interval}", symbol, interval);
            return ApiResponseDto<KLineDataDto?>.CreateError("获取最新K线数据失败");
        }
    }

    public async Task<ApiResponseDto<IEnumerable<KLineDataDto>>> BatchGetKLineDataAsync(
        IEnumerable<string> symbols, 
        string interval, 
        int limit = 100)
    {
        try
        {
            var allData = new List<KLineDataDto>();
            foreach (var symbol in symbols)
            {
                var tradingPairId = await GetTradingPairIdAsync(symbol);
                if (tradingPairId.HasValue)
                {
                    var data = await _repository.GetKLineDataByTradingPairIdAsync(tradingPairId.Value, interval, limit);
                    allData.AddRange(_mapping.MapToDto(data, symbol));
                }
            }
            return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateSuccess(allData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量获取K线数据失败");
            return ApiResponseDto<IEnumerable<KLineDataDto>>.CreateError("批量获取K线数据失败");
        }
    }

    public async Task<ApiResponseDto<KLineDataStatisticsDto>> GetKLineDataStatisticsAsync(
        string symbol, 
        string interval)
    {
        try
        {
            var tradingPairId = await GetTradingPairIdAsync(symbol);
            if (!tradingPairId.HasValue)
            {
                return ApiResponseDto<KLineDataStatisticsDto>.CreateError($"交易对 {symbol} 不存在");
            }

            var data = await _repository.GetKLineDataByTradingPairIdAsync(tradingPairId.Value, interval, 100);
            var dataList = data.ToList();
            
            var stats = new KLineDataStatisticsDto
            {
                TotalRecords = dataList.Count,
                FirstRecordTime = dataList.Any() ? DateTimeOffset.FromUnixTimeMilliseconds(dataList.First().OpenTime).DateTime : null,
                LastRecordTime = dataList.Any() ? DateTimeOffset.FromUnixTimeMilliseconds(dataList.Last().CloseTime).DateTime : null,
                HighestPrice = dataList.Any() ? dataList.Max(k => k.High) : 0,
                LowestPrice = dataList.Any() ? dataList.Min(k => k.Low) : 0,
                TotalVolume = dataList.Sum(k => k.Volume)
            };
            return ApiResponseDto<KLineDataStatisticsDto>.CreateSuccess(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取K线统计数据失败: {Symbol} {Interval}", symbol, interval);
            return ApiResponseDto<KLineDataStatisticsDto>.CreateError("获取K线统计数据失败");
        }
    }

    public Task<ApiResponseDto<IEnumerable<string>>> GetSupportedSymbolsAsync()
    {
        // 简化实现 - 返回常见交易对
        var symbols = new[] { "BTCUSDT", "ETHUSDT", "BNBUSDT" };
        return Task.FromResult(ApiResponseDto<IEnumerable<string>>.CreateSuccess(symbols.AsEnumerable()));
    }

    public Task<ApiResponseDto<IEnumerable<string>>> GetSupportedIntervalsAsync()
    {
        // 简化实现 - 返回支持的时间间隔
        var intervals = new[] { "1m", "5m", "15m", "30m", "1h", "4h", "1d" };
        return Task.FromResult(ApiResponseDto<IEnumerable<string>>.CreateSuccess(intervals.AsEnumerable()));
    }

    public Task<ApiResponseDto<bool>> SubscribeKLineDataAsync(string symbol, string interval)
    {
        // 简化实现 - 暂不支持订阅
        _logger.LogWarning("K线订阅功能尚未实现: {Symbol} {Interval}", symbol, interval);
        return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(true));
    }

    public Task<ApiResponseDto<bool>> UnsubscribeKLineDataAsync(string symbol, string interval)
    {
        // 简化实现 - 暂不支持取消订阅
        return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(true));
    }
}
