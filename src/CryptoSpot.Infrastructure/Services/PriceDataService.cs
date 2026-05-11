using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    public class PriceDataService : IPriceDataService
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IDtoMappingService _mappingService;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ILogger<PriceDataService> _logger;

        public PriceDataService(
            ITradingPairRepository tradingPairRepository,
            IDtoMappingService mappingService,
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ILogger<PriceDataService> logger)
        {
            _tradingPairRepository = tradingPairRepository;
            _mappingService = mappingService;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        public async Task<TradingPairDto?> GetCurrentPriceAsync(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return null;

            try
            {
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                return tradingPair == null ? null : _mappingService.MapToDto(tradingPair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get price for {Symbol}", symbol);
                return null;
            }
        }

        public async Task<IEnumerable<TradingPairDto>> GetCurrentPricesAsync(string[] symbols)
        {
            if (symbols == null || symbols.Length == 0)
                return Enumerable.Empty<TradingPairDto>();

            var normalized = symbols
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalized.Length == 0)
                return Enumerable.Empty<TradingPairDto>();

            await using var context = await _dbContextFactory.CreateDbContextAsync();
            var pairs = await context.Set<TradingPair>()
                .AsNoTracking()
                .Where(tp => normalized.Contains(tp.Symbol))
                .ToListAsync();

            return _mappingService.MapToDto(pairs);
        }

        public async Task<IEnumerable<TradingPairDto>> GetTopTradingPairsAsync(int count = 10)
        {
            if (count <= 0)
            {
                return Enumerable.Empty<TradingPairDto>();
            }

            try
            {
                var tradingPairs = await _tradingPairRepository.GetTopTradingPairsAsync(count);
                return _mappingService.MapToDto(tradingPairs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load top {Count} trading pairs", count);
                return Enumerable.Empty<TradingPairDto>();
            }
        }

        public async Task UpdateTradingPairPriceAsync(
            string symbol,
            decimal price,
            decimal change24h,
            decimal volume24h,
            decimal high24h,
            decimal low24h)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            try
            {
                var updated = await _tradingPairRepository.UpdatePriceAsync(
                    symbol,
                    price,
                    change24h,
                    volume24h,
                    high24h,
                    low24h);

                if (!updated)
                {
                    _logger.LogWarning("Update price failed because trading pair {Symbol} was not found", symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update price for {Symbol}", symbol);
                throw;
            }
        }

        public async Task BatchUpdateTradingPairPricesAsync(IEnumerable<TradingPairDto> tradingPairs)
        {
            if (tradingPairs == null)
            {
                return;
            }

            foreach (var tradingPair in tradingPairs)
            {
                if (string.IsNullOrWhiteSpace(tradingPair.Symbol))
                {
                    continue;
                }

                await UpdateTradingPairPriceAsync(
                    tradingPair.Symbol,
                    tradingPair.Price,
                    tradingPair.Change24h,
                    tradingPair.Volume24h,
                    tradingPair.High24h,
                    tradingPair.Low24h);
            }
        }
    }
}
