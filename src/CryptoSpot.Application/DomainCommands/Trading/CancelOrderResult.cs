namespace CryptoSpot.Application.DomainCommands.Trading
{
    public class CancelOrderResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public static CancelOrderResult CreateSuccess() => new CancelOrderResult { Success = true };
        public static CancelOrderResult CreateFailure(string message) => new CancelOrderResult { Success = false, ErrorMessage = message };
    }
}
