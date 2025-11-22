namespace CryptoSpot.Domain.Exceptions
{
    /// <summary>
    /// 业务异常 - 用于所有业务规则违反的场景
    /// </summary>
    public class BusinessException : DomainException
    {
        public BusinessException(string message) 
            : base(message)
        {
        }

        public BusinessException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
