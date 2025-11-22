namespace CryptoSpot.Domain.Exceptions
{
    /// <summary>
    /// 余额不足异常
    /// </summary>
    public class InsufficientBalanceException : DomainException
    {
        public string Currency { get; }
        public decimal Required { get; }
        public decimal Available { get; }

        public InsufficientBalanceException(string currency, decimal required, decimal available)
            : base($"Insufficient {currency} balance. Required: {required}, Available: {available}")
        {
            Currency = currency;
            Required = required;
            Available = available;
        }
    }
}
