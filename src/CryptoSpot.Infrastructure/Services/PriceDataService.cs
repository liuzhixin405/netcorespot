using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories; // replaced Core.Interfaces.Repositories
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Infrastructure.Services
{
    public class PriceDataService : IPriceDataService
    {
        private readonly ITradingPairService _tradingPairService;
        private readonly ILogger<PriceDataService> _logger;
        private readonly IDtoMappingService _mapping;

        public PriceDataService(
            ITradingPairService tradingPairService,
            ILogger<PriceDataService> logger,
            IDtoMappingService mapping)
        {
            _tradingPairService = tradingPairService;
            _logger = logger;
            _mapping = mapping;
        }

        public async Task<TradingPairDto?> GetCurrentPriceAsync(string symbol)
        {
            try
            {
                var resp = await _tradingPairService.GetTradingPairAsync(symbol);
                return resp.Success ? resp.Data : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current price for {Symbol}", symbol);
                return null;
            }
        }

        public async Task<IEnumerable<TradingPairDto>> GetCurrentPricesAsync(string[] symbols)
        {
            try
            {
                var list = new List<TradingPairDto>();
                foreach (var symbol in symbols)
                {
                    var resp = await _tradingPairService.GetTradingPairAsync(symbol);
                    if (resp.Success && resp.Data != null)
                    {
                        list.Add(resp.Data);
                    }
                }
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current prices for symbols: {Symbols}", string.Join(", ", symbols));
                return Enumerable.Empty<TradingPairDto>();
            }
        }

        public async Task<IEnumerable<TradingPairDto>> GetTopTradingPairsAsync(int count = 10)
        {
            try
            {
                var resp = await _tradingPairService.GetTopTradingPairsAsync(count);
                return resp.Success && resp.Data != null ? resp.Data : Enumerable.Empty<TradingPairDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting top trading pairs");
                return Enumerable.Empty<TradingPairDto>();
            }
        }

        public async Task UpdateTradingPairPriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            try
            {
                await _tradingPairService.UpdatePriceAsync(symbol, price, change24h, volume24h, high24h, low24h);
                _logger.LogDebug("Updated price for {Symbol}: {Price}", symbol, price);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating price for {Symbol}", symbol);
            }
        }

        public async Task BatchUpdateTradingPairPricesAsync(IEnumerable<TradingPairDto> tradingPairs)
        {
            try
            {
                foreach (var dto in tradingPairs)
                {
                    await UpdateTradingPairPriceAsync(
                        dto.Symbol,
                        dto.Price,
                        dto.Change24h,
                        dto.Volume24h,
                        dto.High24h,
                        dto.Low24h);
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
