using CryptoSpot.Core.Entities;

namespace CryptoSpot.Core.Interfaces.Trading
{
    /// <summary>
    /// 交易服务接口
    /// </summary>
    public interface ITradeService
    {
        /// <summary>
        /// 执行交易
        /// </summary>
        Task<Trade> ExecuteTradeAsync(Order buyOrder, Order sellOrder, decimal price, decimal quantity);
        
        /// <summary>
        /// 获取交易历史
        /// </summary>
        Task<IEnumerable<Trade>> GetTradeHistoryAsync(int userId, string? symbol = null, int limit = 100);
        
        /// <summary>
        /// 获取用户交易记录
        /// </summary>
        Task<IEnumerable<Trade>> GetUserTradesAsync(int userId, string symbol = "", int limit = 100);
        
        /// <summary>
        /// 获取最近交易记录
        /// </summary>
        Task<IEnumerable<Trade>> GetRecentTradesAsync(string symbol, int limit = 50);
        
        /// <summary>
        /// 获取交易详情
        /// </summary>
        Task<Trade?> GetTradeByIdAsync(long tradeId);
        
        /// <summary>
        /// 获取订单的交易记录
        /// </summary>
        Task<IEnumerable<Trade>> GetTradesByOrderIdAsync(int orderId);
        
        /// <summary>
        /// 新增别名方法，方便上层调用
        /// </summary>
        Task<IEnumerable<Trade>> GetOrderTradesAsync(int orderId) => GetTradesByOrderIdAsync(orderId);
        
        /// <summary>
        /// 获取交易统计
        /// </summary>
        Task<decimal> GetTradingVolumeAsync(string symbol, TimeSpan timeRange);
        
        /// <summary>
        /// 获取价格范围
        /// </summary>
        Task<(decimal high, decimal low)> GetPriceRangeAsync(string symbol, TimeSpan timeRange);
    }
}
