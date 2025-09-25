namespace CryptoSpot.Application.DomainCommands.Trading
{
    public class UpdatePriceResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public static UpdatePriceResult CreateSuccess() => new UpdatePriceResult { Success = true };
        public static UpdatePriceResult CreateFailure(string msg) => new UpdatePriceResult { Success = false, ErrorMessage = msg };
    }
}
