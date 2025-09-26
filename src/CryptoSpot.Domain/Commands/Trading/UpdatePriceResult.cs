namespace CryptoSpot.Core.Commands.Trading
{
    /// <summary>
    /// 更新价格命令结果
    /// </summary>
    public class UpdatePriceResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public static UpdatePriceResult CreateSuccess()
        {
            return new UpdatePriceResult
            {
                Success = true
            };
        }

        public static UpdatePriceResult CreateFailure(string errorMessage)
        {
            return new UpdatePriceResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
