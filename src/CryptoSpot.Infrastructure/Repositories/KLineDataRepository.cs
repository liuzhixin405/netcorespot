using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CryptoSpot.Infrastructure.Repositories
{
    public class KLineDataRepository : Repository<KLineData>, IKLineDataRepository
    {
        public KLineDataRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<KLineData>> GetBySymbolAndTimeFrameAsync(string symbol, string timeFrame, int limit = 100)
        {
            return await _dbSet
                .Include(k => k.TradingPair)
                .Where(k => k.TradingPair.Symbol == symbol && k.TimeFrame == timeFrame)
                .OrderByDescending(k => k.OpenTime)
                .Take(limit)
                .OrderBy(k => k.OpenTime)
                .ToListAsync();
        }

        public async Task<KLineData?> GetLatestAsync(string symbol, string timeFrame)
        {
            return await _dbSet
                .Include(k => k.TradingPair)
                .Where(k => k.TradingPair.Symbol == symbol && k.TimeFrame == timeFrame)
                .OrderByDescending(k => k.OpenTime)
                .FirstOrDefaultAsync();
        }

        public async Task AddOrUpdateAsync(KLineData klineData)
        {
            var existing = await _dbSet
                .FirstOrDefaultAsync(k => k.TradingPairId == klineData.TradingPairId && 
                                         k.TimeFrame == klineData.TimeFrame && 
                                         k.OpenTime == klineData.OpenTime);

            if (existing != null)
            {
                existing.Close = klineData.Close;
                existing.High = Math.Max(existing.High, klineData.High);
                existing.Low = Math.Min(existing.Low, klineData.Low);
                existing.Volume += klineData.Volume;
                existing.CloseTime = klineData.CloseTime;
                
                _dbSet.Update(existing);
            }
            else
            {
                await _dbSet.AddAsync(klineData);
            }

            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<KLineData>> GetRecentDataAsync(string symbol, string timeFrame, DateTime fromTime)
        {
            var fromTimestamp = ((DateTimeOffset)fromTime).ToUnixTimeMilliseconds();
            
            return await _dbSet
                .Include(k => k.TradingPair)
                .Where(k => k.TradingPair.Symbol == symbol && 
                           k.TimeFrame == timeFrame && 
                           k.OpenTime >= fromTimestamp)
                .OrderBy(k => k.OpenTime)
                .ToListAsync();
        }
    }
}
