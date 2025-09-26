using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Core.Interfaces.MarketData
{
    /// <summary>
    /// 价格数据服务接口
    /// </summary>
    public interface IPriceDataService
    {
        /// <summary>
        /// 获取交易对当前价格
        /// </summary>
        Task<TradingPair?> GetCurrentPriceAsync(string symbol);
        
        /// <summary>
        /// 获取多个交易对的当前价格
        /// </summary>
        Task<IEnumerable<TradingPair>> GetCurrentPricesAsync(string[] symbols);
        
        /// <summary>
        /// 获取热门交易对
        /// </summary>
        Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int count = 10);
        
        /// <summary>
        /// 更新交易对价格
        /// </summary>
        Task UpdateTradingPairPriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);
        
        /// <summary>
        /// 批量更新交易对价格
        /// </summary>
        Task BatchUpdateTradingPairPricesAsync(IEnumerable<TradingPair> tradingPairs);
    }
}
