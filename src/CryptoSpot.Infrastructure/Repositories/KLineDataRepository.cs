using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CryptoSpot.Infrastructure.Repositories
{
    public class KLineDataRepository : BaseRepository<KLineData>, IKLineDataRepository
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IMemoryCache _cache;
        private static readonly string TradingPairCachePrefix = "TradingPairId:";

        public KLineDataRepository(ApplicationDbContext context, ITradingPairRepository tradingPairRepository, IMemoryCache cache) : base(context)
        {
            _tradingPairRepository = tradingPairRepository;
            _cache = cache;
        }

        public async Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100)
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            return await _dbSet
                .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
                .OrderByDescending(k => k.OpenTime)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<IEnumerable<KLineData>> GetKLineDataByTimeRangeAsync(string symbol, string interval, long startTime, long endTime)
        {
            var tradingPairId = await ResolveTradingPairIdAsync(symbol);
            return await _dbSet
                .Where(k => k.TradingPairId == tradingPairId && 
                           k.TimeFrame == interval &&
                           k.OpenTime >= startTime && 
                           k.CloseTime <= endTime)
                .OrderBy(k => k.OpenTime)
                .ToListAsync();
        }

        public async Task<IEnumerable<KLineData>> GetKLineDataByTradingPairIdAsync(int tradingPairId, string interval, int limit = 100)
        {
            return await _dbSet
                .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
                .OrderByDescending(k => k.OpenTime)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<IEnumerable<KLineData>> GetKLineDataByTimeRangeAsync(int tradingPairId, string interval, DateTime startTime, DateTime endTime)
        {
            var startTimeMs = ((DateTimeOffset)startTime).ToUnixTimeMilliseconds();
            var endTimeMs = ((DateTimeOffset)endTime).ToUnixTimeMilliseconds();
            
            return await _dbSet
                .Where(k => k.TradingPairId == tradingPairId && 
                           k.TimeFrame == interval &&
                           k.OpenTime >= startTimeMs && 
                           k.CloseTime <= endTimeMs)
                .OrderBy(k => k.OpenTime)
                .ToListAsync();
        }

        public async Task<KLineData?> GetLatestKLineDataAsync(int tradingPairId, string interval)
        {
            return await _dbSet
                .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
                .OrderByDescending(k => k.OpenTime)
                .FirstOrDefaultAsync();
        }

        public async Task<int> SaveKLineDataBatchAsync(IEnumerable<KLineData> klineDataList)
        {
            var klineDataArray = klineDataList.ToArray();
            
            // 批量插入新数据
            await _dbSet.AddRangeAsync(klineDataArray);
            
            // 对于已存在的数据，更新价格信息
            foreach (var klineData in klineDataArray)
            {
                var existing = await _dbSet
                    .FirstOrDefaultAsync(k => k.TradingPairId == klineData.TradingPairId && 
                                            k.TimeFrame == klineData.TimeFrame && 
                                            k.OpenTime == klineData.OpenTime);
                
                if (existing != null)
                {
                    existing.Open = klineData.Open;
                    existing.High = klineData.High;
                    existing.Low = klineData.Low;
                    existing.Close = klineData.Close;
                    existing.Volume = klineData.Volume;
                    existing.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    
                    _dbSet.Update(existing);
                }
            }
            
            return klineDataArray.Length;
        }

        public async Task<bool> UpsertKLineDataAsync(KLineData klineData)
        {
            var existing = await _dbSet
                .FirstOrDefaultAsync(k => k.TradingPairId == klineData.TradingPairId && 
                                        k.TimeFrame == klineData.TimeFrame && 
                                        k.OpenTime == klineData.OpenTime);
            
            if (existing != null)
            {
                existing.Open = klineData.Open;
                existing.High = klineData.High;
                existing.Low = klineData.Low;
                existing.Close = klineData.Close;
                existing.Volume = klineData.Volume;
                existing.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _dbSet.Update(existing);
            }
            else
            {
                await _dbSet.AddAsync(klineData);
            }
            
            return true;
        }

        public async Task<int> DeleteExpiredKLineDataAsync(int tradingPairId, string interval, int keepDays = 30)
        {
            var cutoffTime = DateTimeOffset.UtcNow.AddDays(-keepDays).ToUnixTimeMilliseconds();
            
            var expiredData = await _dbSet
                .Where(k => k.TradingPairId == tradingPairId && 
                           k.TimeFrame == interval &&
                           k.OpenTime < cutoffTime)
                .ToListAsync();
            
            if (expiredData.Any())
            {
                _dbSet.RemoveRange(expiredData);
            }
            
            return expiredData.Count;
        }

        public async Task<KLineDataStatistics> GetKLineDataStatisticsAsync(int tradingPairId, string interval)
        {
            var data = await _dbSet
                .Where(k => k.TradingPairId == tradingPairId && k.TimeFrame == interval)
                .ToListAsync();
            
            if (!data.Any())
            {
                return new KLineDataStatistics();
            }
            
            return new KLineDataStatistics
            {
                TotalRecords = data.Count,
                FirstRecordTime = data.Min(k => DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).DateTime),
                LastRecordTime = data.Max(k => DateTimeOffset.FromUnixTimeMilliseconds(k.CloseTime).DateTime),
                HighestPrice = data.Max(k => k.High),
                LowestPrice = data.Min(k => k.Low),
                TotalVolume = data.Sum(k => k.Volume)
            };
        }

        private async Task<int> ResolveTradingPairIdAsync(string symbol)
        {
            var key = TradingPairCachePrefix + symbol;
            if (_cache.TryGetValue<int>(key, out var id)) return id;
            id = await _tradingPairRepository.GetTradingPairIdAsync(symbol);
            _cache.Set(key, id, TimeSpan.FromMinutes(5));
            return id;
        }
    }
}