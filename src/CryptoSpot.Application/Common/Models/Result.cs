namespace CryptoSpot.Application.Common.Models
{
    /// <summary>
    /// 统一返回结果
    /// </summary>
    public class Result
    {
        public bool IsSuccess { get; protected set; }
        public bool IsFailure => !IsSuccess;
        public string Error { get; protected set; } = string.Empty;

        protected Result(bool isSuccess, string error)
        {
            if (isSuccess && !string.IsNullOrEmpty(error))
                throw new InvalidOperationException("Success result cannot have error");
            if (!isSuccess && string.IsNullOrEmpty(error))
                throw new InvalidOperationException("Failure result must have error");

            IsSuccess = isSuccess;
            Error = error;
        }

        public static Result Success() => new(true, string.Empty);
        public static Result Failure(string error) => new(false, error);
    }

    /// <summary>
    /// 带返回值的统一结果
    /// </summary>
    public class Result<T> : Result
    {
        public T? Value { get; private set; }

        private Result(bool isSuccess, T? value, string error) : base(isSuccess, error)
        {
            Value = value;
        }

        public static Result<T> Success(T value) => new(true, value, string.Empty);
        public new static Result<T> Failure(string error) => new(false, default, error);
    }
}
