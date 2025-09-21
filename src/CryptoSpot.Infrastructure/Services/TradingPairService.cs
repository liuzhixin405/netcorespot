using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Interfaces.Caching;
using CryptoSpot.Core.Extensions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace CryptoSpot.Infrastructure.Services
{
    public class TradingPairService : ITradingPairService, IDisposable
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly ICacheService _cacheService;
        private readonly ICacheEventService _cacheEventService;
        private readonly ILogger<TradingPairService> _logger;

        public TradingPairService(
            ITradingPairRepository tradingPairRepository,
            ICacheService cacheService,
            ICacheEventService cacheEventService,
            ILogger<TradingPairService> logger)
        {
            _tradingPairRepository = tradingPairRepository;
            _cacheService = cacheService;
            _cacheEventService = cacheEventService;
            _logger = logger;
        }

        public async Task<TradingPair?> GetTradingPairAsync(string symbol)
        {
            return await _cacheService.GetCachedTradingPairBySymbolAsync(symbol);
        }

        public async Task<TradingPair?> GetTradingPairByIdAsync(int tradingPairId)
        {
            var allTradingPairs = await _cacheService.GetCachedTradingPairsAsync();
            return allTradingPairs.FirstOrDefault(tp => tp.Id == tradingPairId);
        }

        public async Task<int> GetTradingPairIdAsync(string symbol)
        {
            var tradingPair = await _cacheService.GetCachedTradingPairBySymbolAsync(symbol);
            return tradingPair?.Id ?? 0;
        }

        public async Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync()
        {
            var allTradingPairs = await _cacheService.GetCachedTradingPairsAsync();
            return allTradingPairs.Where(tp => tp.IsActive);
        }

        public async Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int count = 10)
        {
            var allTradingPairs = await _cacheService.GetCachedTradingPairsAsync();
            return allTradingPairs
                .Where(tp => tp.IsActive)
                .OrderByDescending(tp => tp.Volume24h)
                .Take(count);
        }

        public async Task UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            try
            {
                await _tradingPairRepository.UpdatePriceAsync(symbol, price, change24h, volume24h, high24h, low24h);
                
                // 通知缓存服务数据已变更
                await _cacheEventService.NotifyTradingPairChangedAsync(symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating price for {Symbol}", symbol);
                throw;
            }
        }

        public void Dispose()
        {
            // 没有需要释放的资源
        }
    }
}
