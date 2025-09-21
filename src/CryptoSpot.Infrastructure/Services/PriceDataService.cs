using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class PriceDataService : IPriceDataService
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly ILogger<PriceDataService> _logger;

        public PriceDataService(
            ITradingPairRepository tradingPairRepository,
            ILogger<PriceDataService> logger)
        {
            _tradingPairRepository = tradingPairRepository;
            _logger = logger;
        }

        public async Task<TradingPair?> GetCurrentPriceAsync(string symbol)
        {
            try
            {
                return await _tradingPairRepository.GetBySymbolAsync(symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current price for {Symbol}", symbol);
                return null;
            }
        }

        public async Task<IEnumerable<TradingPair>> GetCurrentPricesAsync(string[] symbols)
        {
            try
            {
                var results = new List<TradingPair>();
                foreach (var symbol in symbols)
                {
                    var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                    if (tradingPair != null)
                    {
                        results.Add(tradingPair);
                    }
                }
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current prices for symbols: {Symbols}", string.Join(", ", symbols));
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

        public async Task UpdateTradingPairPriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            try
            {
                await _tradingPairRepository.UpdatePriceAsync(symbol, price, change24h, volume24h, high24h, low24h);
                _logger.LogDebug("Updated price for {Symbol}: {Price}", symbol, price);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating price for {Symbol}", symbol);
            }
        }

        public async Task BatchUpdateTradingPairPricesAsync(IEnumerable<TradingPair> tradingPairs)
        {
            try
            {
                foreach (var tradingPair in tradingPairs)
                {
                    await UpdateTradingPairPriceAsync(
                        tradingPair.Symbol,
                        tradingPair.Price,
                        tradingPair.Change24h,
                        tradingPair.Volume24h,
                        tradingPair.High24h,
                        tradingPair.Low24h);
                }
                
                _logger.LogInformation("Batch updated prices for {Count} trading pairs", tradingPairs.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error batch updating trading pair prices");
            }
        }
    }
}
