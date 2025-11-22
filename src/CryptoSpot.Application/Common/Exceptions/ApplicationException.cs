namespace CryptoSpot.Application.Common.Exceptions
{
    /// <summary>
    /// 应用层异常基类
    /// </summary>
    public abstract class ApplicationException : Exception
    {
        protected ApplicationException(string message) : base(message)
        {
        }

        protected ApplicationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
