namespace CryptoSpot.Application.Common.Exceptions
{
    /// <summary>
    /// 未找到异常
    /// </summary>
    public class NotFoundException : ApplicationException
    {
        public NotFoundException(string entityName, object key)
            : base($"Entity '{entityName}' with key '{key}' was not found")
        {
        }

        public NotFoundException(string message) : base(message)
        {
        }
    }
}
