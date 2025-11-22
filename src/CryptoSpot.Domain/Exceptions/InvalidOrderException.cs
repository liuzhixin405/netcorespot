namespace CryptoSpot.Domain.Exceptions
{
    /// <summary>
    /// 无效订单异常
    /// </summary>
    public class InvalidOrderException : DomainException
    {
        public InvalidOrderException(string message) : base(message)
        {
        }
    }
}
