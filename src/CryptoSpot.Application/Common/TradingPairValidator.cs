using CryptoSpot.Domain.Entities;
using CryptoSpot.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Common
{
    /// <summary>
    /// 交易对验证器 - 提供通用的交易对验证逻辑
    /// </summary>
    public class TradingPairValidator
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly ILogger<TradingPairValidator> _logger;

        public TradingPairValidator(
            ITradingPairRepository tradingPairRepository,
            ILogger<TradingPairValidator> logger)
        {
            _tradingPairRepository = tradingPairRepository;
            _logger = logger;
        }

        /// <summary>
        /// 验证交易对是否存在
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <returns>交易对对象，如果不存在则返回null</returns>
        public async Task<TradingPair?> ValidateTradingPairAsync(string symbol)
        {
            try
            {
                var tradingPair = await _tradingPairRepository.GetBySymbolAsync(symbol);
                if (tradingPair == null)
                {
                    _logger.LogWarning("交易对不存在: {Symbol}", symbol);
                }
                return tradingPair;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证交易对时出错: {Symbol}", symbol);
                return null;
            }
        }

        /// <summary>
        /// 验证交易对是否存在，如果不存在则抛出异常
        /// </summary>
        /// <param name="symbol">交易对符号</param>
        /// <returns>交易对对象</returns>
        /// <exception cref="ArgumentException">交易对不存在时抛出</exception>
        public async Task<TradingPair> ValidateTradingPairOrThrowAsync(string symbol)
        {
            var tradingPair = await ValidateTradingPairAsync(symbol);
            if (tradingPair == null)
            {
                throw new ArgumentException($"交易对 {symbol} 不存在");
            }
            return tradingPair;
        }
    }
}
