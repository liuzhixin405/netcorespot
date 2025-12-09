using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Infrastructure.MatchEngine.Services
{
    /// <summary>
    /// 交易对符号解析服务接口
    /// </summary>
    public interface ITradingPairParser
    {
        /// <summary>
        /// 解析交易对符号，提取基础货币和报价货币
        /// </summary>
        (string baseCurrency, string quoteCurrency) ParseSymbol(string symbol);

        /// <summary>
        /// 根据订单类型计算需要冻结的货币和金额
        /// </summary>
        (string currency, decimal amount) GetFreezeAmount(Order order, string symbol);
    }
}
