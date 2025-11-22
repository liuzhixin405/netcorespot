namespace CryptoSpot.Domain.Exceptions
{
    /// <summary>
    /// 未授权异常
    /// </summary>
    public class UnauthorizedException : DomainException
    {
        public UnauthorizedException()
            : base("未授权的操作")
        {
        }

        public UnauthorizedException(string message)
            : base(message)
        {
        }
    }
}
