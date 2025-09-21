using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Extensions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace CryptoSpot.Infrastructure.Services
{
    public class TradingPairService : ITradingPairService, IDisposable
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly ILogger<TradingPairService> _logger;
        private readonly ConcurrentDictionary<string, int> _tradingPairIdCache = new();
        private readonly ConcurrentDictionary<string, TradingPair> _tradingPairCache = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public TradingPairService(
            ITradingPairRepository tradingPairRepository,
            ILogger<TradingPairService> logger)
        {
            _tradingPairRepository = tradingPairRepository;
            _logger = logger;
        }

        public async Task<TradingPair?> GetTradingPairAsync(string symbol)
        {
            if (_tradingPairCache.TryGetValue(symbol, out var cachedPair))
            {
                return cachedPair;
            }

            await _semaphore.WaitAsync();
            try
            {
                // 双重检查，防止并发问题
                if (_tradingPairCache.TryGetValue(symbol, out var cachedPair2))
                {
                    return cachedPair2;
                }

                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (tradingPair != null)
                {
                    _tradingPairCache[symbol] = tradingPair;
                    _tradingPairIdCache[symbol] = tradingPair.Id;
                }

                return tradingPair;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<int> GetTradingPairIdAsync(string symbol)
        {
            if (_tradingPairIdCache.TryGetValue(symbol, out var cachedId))
            {
                return cachedId;
            }

            await _semaphore.WaitAsync();
            try
            {
                // 双重检查，防止并发问题
                if (_tradingPairIdCache.TryGetValue(symbol, out var cachedId2))
                {
                    return cachedId2;
                }

                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (tradingPair != null)
                {
                    _tradingPairIdCache[symbol] = tradingPair.Id;
                    _tradingPairCache[symbol] = tradingPair;
                    return tradingPair.Id;
                }

                return 0;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync()
        {
            try
            {
                return await _tradingPairRepository.GetActivePairsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active trading pairs");
                return new List<TradingPair>();
            }
        }

        public async Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int count = 10)
        {
            try
            {
                return await _tradingPairRepository.GetTopPairsAsync(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting top trading pairs");
                return new List<TradingPair>();
            }
        }

        public async Task UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            try
            {
                await _tradingPairRepository.UpdatePriceAsync(symbol, price, change24h, volume24h, high24h, low24h);
                
                // 更新缓存
                if (_tradingPairCache.TryGetValue(symbol, out var cachedPair))
                {
                    cachedPair.Price = price;
                    cachedPair.Change24h = change24h;
                    cachedPair.Volume24h = volume24h;
                    cachedPair.High24h = high24h;
                    cachedPair.Low24h = low24h;
                    cachedPair.LastUpdated = DateTimeExtensions.GetCurrentUnixTimeMilliseconds();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating price for {Symbol}", symbol);
                throw;
            }
        }

        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }
}
