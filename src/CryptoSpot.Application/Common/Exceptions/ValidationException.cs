namespace CryptoSpot.Application.Common.Exceptions
{
    /// <summary>
    /// 验证异常
    /// </summary>
    public class ValidationException : ApplicationException
    {
        public Dictionary<string, string[]> Errors { get; }

        public ValidationException(Dictionary<string, string[]> errors)
            : base("One or more validation errors occurred")
        {
            Errors = errors;
        }

        public ValidationException(string propertyName, string error)
            : base($"Validation error for {propertyName}: {error}")
        {
            Errors = new Dictionary<string, string[]>
            {
                [propertyName] = new[] { error }
            };
        }
    }
}
