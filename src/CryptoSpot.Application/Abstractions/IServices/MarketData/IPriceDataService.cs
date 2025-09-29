using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.Abstractions.Services.MarketData
{
    public interface IPriceDataService
    {
        // 获取单个/多个当前价格，改为使用 DTO
        Task<TradingPairDto?> GetCurrentPriceAsync(string symbol);
        Task<IEnumerable<TradingPairDto>> GetCurrentPricesAsync(string[] symbols);
        Task<IEnumerable<TradingPairDto>> GetTopTradingPairsAsync(int count = 10);

        // 更新价格（保持输入为基础字段）
        Task UpdateTradingPairPriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);

        // 批量更新：可传入 DTO（外部来源）
        Task BatchUpdateTradingPairPricesAsync(IEnumerable<TradingPairDto> tradingPairs);
    }
}
