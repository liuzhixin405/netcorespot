// filepath: g:\github\netcorespot\src\CryptoSpot.Infrastructure\Services\KLineDataDomainService.cs
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 领域 K 线数据服务实现：直接返回 / 操作领域实体，不做 DTO 映射。
    /// </summary>
    public class KLineDataDomainService : IKLineDataDomainService
    {
        private readonly IKLineDataRepository _klineRepository;
        private readonly ITradingPairService _tradingPairService;
        private readonly ILogger<KLineDataDomainService> _logger;

        public KLineDataDomainService(
            IKLineDataRepository klineRepository,
            ITradingPairService tradingPairService,
            ILogger<KLineDataDomainService> logger)
        {
            _klineRepository = klineRepository;
            _tradingPairService = tradingPairService;
            _logger = logger;
        }

        public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
        {
            try
            {
                return await _klineRepository.GetKLineDataAsync(symbol, interval, limit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取K线数据失败 {Symbol} {Interval}", symbol, interval);
                return Enumerable.Empty<KLineData>();
            }
        }

        public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, long? startTime, long? endTime, int limit = 100)
        {
            // 若未提供时间范围直接退化为普通查询
            if (startTime == null || endTime == null)
            {
                return await GetKLineDataAsync(symbol, interval, limit);
            }
            try
            {
                var start = DateTimeOffset.FromUnixTimeMilliseconds(startTime.Value).UtcDateTime;
                var end = DateTimeOffset.FromUnixTimeMilliseconds(endTime.Value).UtcDateTime;
                var tradingPairId = await _tradingPairService.GetTradingPairIdAsync(symbol);
                if (tradingPairId <= 0) return Enumerable.Empty<KLineData>();
                return await _klineRepository.GetKLineDataByTimeRangeAsync(tradingPairId, interval, start, end);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按时间范围获取K线数据失败 {Symbol} {Interval}", symbol, interval);
                return Enumerable.Empty<KLineData>();
            }
        }

        public async Task<IEnumerable<KLineData>> GetHistoricalKLineDataAsync(string symbol, string interval, long startTime, long endTime)
        {
            try
            {
                var start = DateTimeOffset.FromUnixTimeMilliseconds(startTime).UtcDateTime;
                var end = DateTimeOffset.FromUnixTimeMilliseconds(endTime).UtcDateTime;
                var tradingPairId = await _tradingPairService.GetTradingPairIdAsync(symbol);
                if (tradingPairId <= 0) return Enumerable.Empty<KLineData>();
                return await _klineRepository.GetKLineDataByTimeRangeAsync(tradingPairId, interval, start, end);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取历史K线数据失败 {Symbol} {Interval}", symbol, interval);
                return Enumerable.Empty<KLineData>();
            }
        }

        public async Task<KLineData?> GetLatestKLineDataAsync(string symbol, string interval)
        {
            try
            {
                var tradingPairId = await _tradingPairService.GetTradingPairIdAsync(symbol);
                if (tradingPairId <= 0) return null;
                return await _klineRepository.GetLatestKLineDataAsync(tradingPairId, interval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最新K线失败 {Symbol} {Interval}", symbol, interval);
                return null;
            }
        }

        public async Task<KLineData> AddOrUpdateKLineDataAsync(KLineData klineData)
        {
            try
            {
                await _klineRepository.UpsertKLineDataAsync(klineData);
                return klineData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "新增或更新K线失败 TradingPairId={TpId} {Interval} {Open}", klineData.TradingPairId, klineData.TimeFrame, klineData.OpenTime);
                throw;
            }
        }

        public async Task<IEnumerable<KLineData>> BatchAddOrUpdateKLineDataAsync(IEnumerable<KLineData> klineDataList)
        {
            var list = klineDataList.ToList();
            if (!list.Any()) return list;
            try
            {
                await _klineRepository.SaveKLineDataBatchAsync(list);
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量新增或更新K线失败 Count={Count}", list.Count);
                return list; // 返回已有引用（可能部分已成功）
            }
        }

        public async Task SaveKLineDataAsync(KLineData klineData)
        {
            await AddOrUpdateKLineDataAsync(klineData);
        }
    }
}
