namespace CryptoSpot.Application.DomainCommands.Trading
{
    public class ProcessKLineDataResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public static ProcessKLineDataResult CreateSuccess() => new ProcessKLineDataResult { Success = true };
        public static ProcessKLineDataResult CreateFailure(string msg) => new ProcessKLineDataResult { Success = false, ErrorMessage = msg };
    }
}
