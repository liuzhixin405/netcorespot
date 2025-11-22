namespace CryptoSpot.Domain.Exceptions
{
    /// <summary>
    /// 交易对未激活异常
    /// </summary>
    public class TradingPairNotActiveException : DomainException
    {
        public string Symbol { get; }

        public TradingPairNotActiveException(string symbol)
            : base($"Trading pair '{symbol}' is not active")
        {
            Symbol = symbol;
        }
    }
}
