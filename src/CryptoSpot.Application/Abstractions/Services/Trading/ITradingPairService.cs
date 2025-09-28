using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Services.Trading
{
    public interface ITradingPairService
    {
        // 返回 Domain 实体，DTO 由上层 (ITradingService / MappingService) 负责转换
        Task<TradingPair?> GetTradingPairAsync(string symbol);
        Task<TradingPair?> GetTradingPairByIdAsync(int tradingPairId);
        Task<int> GetTradingPairIdAsync(string symbol);
        Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync();
        Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int count = 10);
        Task UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);
    }
}
