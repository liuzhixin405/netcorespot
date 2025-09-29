using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Application.DTOs.Common;

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    public interface ITradingPairService
    {
        Task<ApiResponseDto<TradingPairDto?>> GetTradingPairAsync(string symbol);
        Task<ApiResponseDto<TradingPairDto?>> GetTradingPairByIdAsync(int tradingPairId);
        Task<ApiResponseDto<int>> GetTradingPairIdAsync(string symbol);
        Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetActiveTradingPairsAsync();
        Task<ApiResponseDto<IEnumerable<TradingPairDto>>> GetTopTradingPairsAsync(int count = 10);
        Task<ApiResponseDto<bool>> UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);
    }
}
