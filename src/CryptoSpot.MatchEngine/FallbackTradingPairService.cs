using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.DTOs.Trading;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CryptoSpot.MatchEngine
{
    // Minimal TradingPairService used by MatchEngine host when MySQL persistence is not available.
    public class FallbackTradingPairService : ITradingPairService
    {
        // Always returns trading pair id 1 for any symbol (best-effort for local testing).
        public Task<ApiResponseDto<TradingPairDto?>> GetTradingPairAsync(string symbol)
        {
            var dto = new TradingPairDto { Id = 1, Symbol = symbol, IsActive = true };
            return Task.FromResult(ApiResponseDto<TradingPairDto?>.CreateSuccess(dto));
        }

        public Task<ApiResponseDto<TradingPairDto?>> GetTradingPairByIdAsync(long tradingPairId)
        {
            var dto = new TradingPairDto { Id = tradingPairId, Symbol = "TEST", IsActive = true };
            return Task.FromResult(ApiResponseDto<TradingPairDto?>.CreateSuccess(dto));
        }

        public Task<ApiResponseDto<long>> GetTradingPairIdAsync(string symbol)
        {
            return Task.FromResult(ApiResponseDto<long>.CreateSuccess(1));
        }

        public Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetActiveTradingPairsAsync()
        {
            var list = new List<TradingPairDto> { new TradingPairDto { Id = 1, Symbol = "TEST", IsActive = true } };
            return Task.FromResult(ApiResponseDto<IEnumerable<TradingPairDto>>.CreateSuccess(list));
        }

        public Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetTopTradingPairsAsync(int count = 10)
        {
            var list = new List<TradingPairDto> { new TradingPairDto { Id = 1, Symbol = "TEST", IsActive = true } };
            return Task.FromResult(ApiResponseDto<IEnumerable<TradingPairDto>>.CreateSuccess(list));
        }

        public Task<ApiResponseDto<bool>> UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h)
        {
            return Task.FromResult(ApiResponseDto<bool>.CreateSuccess(true));
        }
    }
}
