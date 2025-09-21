using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class KLineDataService : IKLineDataService
    {
        private readonly IKLineDataRepository _klineDataRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly ILogger<KLineDataService> _logger;

        public KLineDataService(
            IKLineDataRepository klineDataRepository,
            ITradingPairRepository tradingPairRepository,
            ILogger<KLineDataService> logger)
        {
            _klineDataRepository = klineDataRepository;
            _tradingPairRepository = tradingPairRepository;
            _logger = logger;
        }

        public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
        {
            try
            {
                return await _klineDataRepository.GetBySymbolAndTimeFrameAsync(symbol, interval, limit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting K-line data for {Symbol} {Interval}", symbol, interval);
                return new List<KLineData>();
            }
        }

        public async Task<IEnumerable<KLineData>> GetHistoricalKLineDataAsync(string symbol, string interval, DateTime startTime, DateTime endTime)
        {
            try
            {
                // 将 DateTime 转换为时间戳进行查询
                var fromTime = startTime;
                var recentData = await _klineDataRepository.GetRecentDataAsync(symbol, interval, fromTime);
                
                // 过滤结束时间
                var endTimestamp = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();
                return recentData.Where(k => k.OpenTime <= endTimestamp);
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
                return await _klineDataRepository.GetLatestAsync(symbol, interval);
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
                await _klineDataRepository.AddOrUpdateAsync(klineData);
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
    }
}
