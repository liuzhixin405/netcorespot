namespace CryptoSpot.Application.Common.Exceptions
{
    /// <summary>
    /// 未授权异常
    /// </summary>
    public class UnauthorizedException : ApplicationException
    {
        public UnauthorizedException(string message) : base(message)
        {
        }

        public UnauthorizedException() : base("Unauthorized access")
        {
        }
    }
}
