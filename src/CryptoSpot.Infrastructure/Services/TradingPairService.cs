using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Trading; // migrated from Core.Interfaces.Trading
using CryptoSpot.Application.Abstractions.Repositories; // replaced Core.Interfaces.Repositories
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class TradingPairService : ITradingPairService, IDisposable
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly RedisCacheService _cacheService;
        private readonly ILogger<TradingPairService> _logger;

        public TradingPairService(
            ITradingPairRepository tradingPairRepository,
            RedisCacheService cacheService,
            ILogger<TradingPairService> logger)
        {
            _tradingPairRepository = tradingPairRepository;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<TradingPair?> GetTradingPairAsync(string symbol)
        {
            try
            {
                // 先从Redis缓存获取
                var cachedPair = await _cacheService.GetTradingPairAsync(symbol);
                if (cachedPair != null)
                {
                    return cachedPair;
                }

                // 缓存中没有，从数据库获取
                var pair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (pair != null)
                {
                    // 缓存到Redis
                    await _cacheService.SetTradingPairAsync(pair);
                }

                return pair;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易对失败: Symbol={Symbol}", symbol);
                return null;
            }
        }

        public async Task<TradingPair?> GetTradingPairByIdAsync(int tradingPairId)
        {
            try
            {
                // 直接从数据库获取（ID查找相对较少）
                var pair = await _tradingPairRepository.GetByIdAsync(tradingPairId);
                if (pair != null)
                {
                    // 缓存到Redis
                    await _cacheService.SetTradingPairAsync(pair);
                }
                return pair;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据ID获取交易对失败: TradingPairId={TradingPairId}", tradingPairId);
                return null;
            }
        }

        public async Task<int> GetTradingPairIdAsync(string symbol)
        {
            try
            {
                var tradingPair = await GetTradingPairAsync(symbol);
                return tradingPair?.Id ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易对ID失败: Symbol={Symbol}", symbol);
                return 0;
            }
        }

        public async Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync()
        {
            try
            {
                // 先尝试从Redis获取所有交易对
                var allTradingPairs = await _cacheService.GetAllTradingPairsAsync();
                
                if (allTradingPairs.Any())
                {
                    return allTradingPairs.Where(tp => tp.IsActive);
                }

                // 如果Redis中没有，从数据库获取
                var activePairs = await _tradingPairRepository.FindAsync(tp => tp.IsActive);
                
                // 批量缓存到Redis
                foreach (var pair in activePairs)
                {
                    await _cacheService.SetTradingPairAsync(pair);
                }
                
                return activePairs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃交易对失败");
                return Enumerable.Empty<TradingPair>();
            }
        }

        public async Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int count = 10)
        {
            try
            {
                var activePairs = await GetActiveTradingPairsAsync();
                return activePairs
                    .OrderByDescending(tp => tp.Volume24h)
                    .Take(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取热门交易对失败");
                return Enumerable.Empty<TradingPair>();
            }
        }

        public async Task UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            try
            {
                // 获取交易对
                var tradingPair = await GetTradingPairAsync(symbol);
                if (tradingPair == null)
                {
                    _logger.LogWarning("尝试更新不存在的交易对价格: Symbol={Symbol}", symbol);
                    return;
                }

                // 更新价格信息
                tradingPair.Price = price;
                tradingPair.Change24h = change24h;
                tradingPair.Volume24h = volume24h;
                tradingPair.High24h = high24h;
                tradingPair.Low24h = low24h;
                tradingPair.LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // 更新数据库
                await _tradingPairRepository.UpdateAsync(tradingPair);

                // 更新Redis缓存
                await _cacheService.SetTradingPairAsync(tradingPair);

                _logger.LogDebug("交易对价格更新成功: Symbol={Symbol}, Price={Price}", symbol, price);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新交易对价格失败: Symbol={Symbol}", symbol);
                throw;
            }
        }

        public void Dispose()
        {
            // Nothing to dispose for now
        }
    }
}
