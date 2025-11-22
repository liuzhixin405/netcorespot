namespace CryptoSpot.Domain.Exceptions
{
    /// <summary>
    /// 未找到资源异常
    /// </summary>
    public class NotFoundException : DomainException
    {
        public NotFoundException(string entityName, object key)
            : base($"实体 '{entityName}' ({key}) 未找到")
        {
        }

        public NotFoundException(string message)
            : base(message)
        {
        }
    }
}
