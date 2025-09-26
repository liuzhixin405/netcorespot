using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Core.Interfaces.MarketData
{
    /// <summary>
    /// 市场数据提供者接口（如Binance、OKX等）
    /// </summary>
    public interface IMarketDataProvider
    {
        /// <summary>
        /// 提供者名称
        /// </summary>
        string ProviderName { get; }
        
        /// <summary>
        /// 是否可用
        /// </summary>
        Task<bool> IsAvailableAsync();
        
        /// <summary>
        /// 获取K线数据
        /// </summary>
        Task<IEnumerable<KLineData>> GetKLineDataAsync(string symbol, string interval, int limit = 100);
        
        /// <summary>
        /// 获取交易对信息
        /// </summary>
        Task<TradingPair?> GetTradingPairAsync(string symbol);
        
        /// <summary>
        /// 获取热门交易对
        /// </summary>
        Task<IEnumerable<TradingPair>> GetTopTradingPairsAsync(int count = 10);
        
        /// <summary>
        /// 开始实时数据同步
        /// </summary>
        Task StartRealTimeDataSyncAsync();
        
        /// <summary>
        /// 停止实时数据同步
        /// </summary>
        Task StopRealTimeDataSyncAsync();
    }
}
