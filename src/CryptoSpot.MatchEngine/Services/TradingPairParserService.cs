using System;
using System.Linq;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.MatchEngine.Services
{
    /// <summary>
    /// 交易对符号解析服务实现
    /// 统一管理交易对符号解析逻辑，支持多种报价货币
    /// </summary>
    public class TradingPairParserService : ITradingPairParser
    {
        private readonly ILogger<TradingPairParserService> _logger;

        // 支持的报价货币列表（按长度降序排列，优先匹配较长的）
        // TODO: 未来可从配置文件或数据库读取
        private static readonly string[] QuoteCurrencies = { "USDT", "BUSD", "USDC", "BTC", "ETH", "BNB" };

        public TradingPairParserService(ILogger<TradingPairParserService> logger)
        {
            _logger = logger;
        }

        public (string baseCurrency, string quoteCurrency) ParseSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
            }

            symbol = symbol.ToUpperInvariant();

            // 按长度降序尝试匹配，优先匹配较长的报价货币
            foreach (var quote in QuoteCurrencies.OrderByDescending(q => q.Length))
            {
                if (symbol.EndsWith(quote, StringComparison.OrdinalIgnoreCase))
                {
                    var baseCurrency = symbol.Substring(0, symbol.Length - quote.Length);
                    
                    if (string.IsNullOrWhiteSpace(baseCurrency))
                    {
                        continue; // 基础货币不能为空
                    }

                    _logger.LogDebug("Parsed symbol {Symbol} -> Base: {Base}, Quote: {Quote}",
                        symbol, baseCurrency, quote);
                    
                    return (baseCurrency.ToUpperInvariant(), quote.ToUpperInvariant());
                }
            }

            // 如果无法解析，默认假设 USDT 为报价货币
            _logger.LogWarning("Unable to parse symbol {Symbol}, defaulting to USDT quote currency", symbol);
            var defaultBase = symbol.Replace("USDT", "", StringComparison.OrdinalIgnoreCase);
            return (defaultBase.ToUpperInvariant(), "USDT");
        }

        public (string currency, decimal amount) GetFreezeAmount(Order order, string symbol)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            var (baseCurrency, quoteCurrency) = ParseSymbol(symbol);
            var price = order.Price ?? 0;

            if (order.Side == OrderSide.Buy)
            {
                // 买单需要冻结报价货币（如 USDT）
                var quoteAmount = order.Quantity * price;
                _logger.LogDebug("Buy order freeze: {Amount} {Currency}", quoteAmount, quoteCurrency);
                return (quoteCurrency, quoteAmount);
            }
            else
            {
                // 卖单需要冻结基础货币（如 BTC）
                _logger.LogDebug("Sell order freeze: {Amount} {Currency}", order.Quantity, baseCurrency);
                return (baseCurrency, order.Quantity);
            }
        }
    }
}
