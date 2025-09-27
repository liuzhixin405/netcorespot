using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories; // replaced Core.Interfaces.Repositories
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.MarketData;

namespace CryptoSpot.Infrastructure.Services
{
    public class KLineDataDomainService : IKLineDataDomainService // 原 KLineDataService : IKLineDataService
    {
        private readonly IKLineDataRepository _klineDataRepository;
        private readonly ILogger<KLineDataDomainService> _logger; // 更新
        private readonly ITradingPairService _tradingPairService;

        public KLineDataDomainService(
            IKLineDataRepository klineDataRepository,
            ILogger<KLineDataDomainService> logger, // 更新
            ITradingPairService tradingPairService)
        {
            _klineDataRepository = klineDataRepository;
            _logger = logger;
            _tradingPairService = tradingPairService;
        }

        public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
        {
            try
            {
                var pair = await _tradingPairService.GetTradingPairAsync(symbol);
                if (pair == null) return Enumerable.Empty<KLineData>();
                return await _klineDataRepository.GetKLineDataByTradingPairIdAsync(pair.Id, interval, limit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting K-line data for {Symbol} {Interval}", symbol, interval);
                return Enumerable.Empty<KLineData>();
            }
        }

        public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, long? startTime, long? endTime, int limit = 100)
        {
            try
            {
                var pair = await _tradingPairService.GetTradingPairAsync(symbol);
                if (pair == null) return Enumerable.Empty<KLineData>();
                var allData = await _klineDataRepository.GetKLineDataByTradingPairIdAsync(pair.Id, interval, limit * 2);
                
                var filteredData = allData.AsQueryable();
                
                if (startTime.HasValue)
                {
                    filteredData = filteredData.Where(k => k.OpenTime >= startTime.Value);
                }
                
                if (endTime.HasValue)
                {
                    filteredData = filteredData.Where(k => k.OpenTime <= endTime.Value);
                }
                
                return filteredData.OrderByDescending(k => k.OpenTime).Take(limit).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting K-line data with time range for {Symbol} {Interval}", symbol, interval);
                return Enumerable.Empty<KLineData>();
            }
        }

        public async Task<IEnumerable<KLineData>> GetHistoricalKLineDataAsync(string symbol, string interval, long startTime, long endTime)
        {
            try
            {
                return await GetKLineDataAsync(symbol, interval, startTime, endTime, 1000);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting historical K-line data for {Symbol} {Interval}", symbol, interval);
                return new List<KLineData>();
            }
        }

        public async Task<KLineData?> GetLatestKLineDataAsync(string symbol, string interval)
        {
            try
            {
                var pair = await _tradingPairService.GetTradingPairAsync(symbol);
                if (pair == null) return null;
                return await _klineDataRepository.GetLatestKLineDataAsync(pair.Id, interval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting latest K-line data for {Symbol} {Interval}", symbol, interval);
                return null;
            }
        }

        public async Task<KLineData> AddOrUpdateKLineDataAsync(KLineData klineData)
        {
            try
            {
                await _klineDataRepository.UpsertKLineDataAsync(klineData);
                _logger.LogDebug("Added/Updated K-line data for trading pair {TradingPairId}, time frame {TimeFrame}, open time {OpenTime}", 
                    klineData.TradingPairId, klineData.TimeFrame, klineData.OpenTime);
                return klineData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding/updating K-line data");
                throw;
            }
        }

        public async Task<IEnumerable<KLineData>> BatchAddOrUpdateKLineDataAsync(IEnumerable<KLineData> klineDataList)
        {
            try
            {
                var results = new List<KLineData>();
                foreach (var klineData in klineDataList)
                {
                    var result = await AddOrUpdateKLineDataAsync(klineData);
                    results.Add(result);
                }
                
                _logger.LogInformation("Batch processed {Count} K-line data entries", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error batch processing K-line data");
                return new List<KLineData>();
            }
        }

        public async Task SaveKLineDataAsync(KLineData klineData)
        {
            try
            {
                await _klineDataRepository.AddAsync(klineData);
                _logger.LogDebug("Saved K-line data for TradingPairId {TradingPairId} {TimeFrame} @ {OpenTime}", 
                    klineData.TradingPairId, klineData.TimeFrame, klineData.OpenTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving K-line data for TradingPairId {TradingPairId} {TimeFrame}", 
                    klineData.TradingPairId, klineData.TimeFrame);
                throw;
            }
        }
    }
}
