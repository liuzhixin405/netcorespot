using CryptoSpot.Domain.Entities;

namespace CryptoSpot.MatchEngine.Services
{
    /// <summary>
    /// 交易对符号解析服务接口
    /// </summary>
    public interface ITradingPairParser
    {
        /// <summary>
        /// 解析交易对符号，提取基础货币和报价货币
        /// </summary>
        /// <param name="symbol">交易对符号，如 BTCUSDT</param>
        /// <returns>基础货币和报价货币</returns>
        (string baseCurrency, string quoteCurrency) ParseSymbol(string symbol);

        /// <summary>
        /// 根据订单类型计算需要冻结的货币和金额
        /// </summary>
        /// <param name="order">订单</param>
        /// <param name="symbol">交易对符号</param>
        /// <returns>货币类型和冻结金额</returns>
        (string currency, decimal amount) GetFreezeAmount(Order order, string symbol);
    }
}
