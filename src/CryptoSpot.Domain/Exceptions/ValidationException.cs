namespace CryptoSpot.Domain.Exceptions
{
    /// <summary>
    /// 验证异常
    /// </summary>
    public class ValidationException : DomainException
    {
        public IDictionary<string, string[]> Errors { get; }

        public ValidationException(IDictionary<string, string[]> errors)
            : base("一个或多个验证错误")
        {
            Errors = errors;
        }

        public ValidationException(string field, string error)
            : base($"{field}: {error}")
        {
            Errors = new Dictionary<string, string[]>
            {
                { field, new[] { error } }
            };
        }
    }
}
