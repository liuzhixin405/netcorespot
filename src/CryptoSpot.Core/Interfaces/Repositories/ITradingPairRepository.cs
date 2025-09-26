using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Core.Interfaces.Repositories
{
    /// <summary>
    /// 交易对仓储接口
    /// </summary>
    public interface ITradingPairRepository : IRepository<TradingPair>
    {
        /// <summary>
        /// 根据符号获取交易对
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <returns>交易对对象</returns>
        Task<TradingPair?> GetBySymbolAsync(string symbol);

        /// <summary>
        /// 获取活跃的交易对
        /// </summary>
        /// <returns>活跃交易对列表</returns>
        Task<IEnumerable<TradingPair>> GetActiveTradingPairsAsync();

        /// <summary>
        /// 获取热门交易对
        /// </summary>
        /// <param name="limit">限制数量</param>
        /// <returns>热门交易对列表</returns>
        Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int limit = 10);

        /// <summary>
        /// 更新交易对价格
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <param name="price">新价格</param>
        /// <param name="change24h">24小时变化</param>
        /// <param name="volume24h">24小时成交量</param>
        /// <param name="high24h">24小时最高价</param>
        /// <param name="low24h">24小时最低价</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdatePriceAsync(string symbol, decimal price, decimal change24h, decimal volume24h, decimal high24h, decimal low24h);

        /// <summary>
        /// 根据符号获取交易对ID
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <returns>交易对ID</returns>
        Task<int> GetTradingPairIdAsync(string symbol);

        /// <summary>
        /// 搜索交易对
        /// </summary>
        /// <param name="keyword">搜索关键词</param>
        /// <param name="limit">限制数量</param>
        /// <returns>匹配的交易对列表</returns>
        Task<IEnumerable<TradingPair>> SearchTradingPairsAsync(string keyword, int limit = 20);
    }
}