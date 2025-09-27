namespace CryptoSpot.Domain.Commands.Trading
{
    /// <summary>
    /// 取消订单命令结果
    /// </summary>
    public class CancelOrderResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public static CancelOrderResult CreateSuccess()
        {
            return new CancelOrderResult
            {
                Success = true
            };
        }

        public static CancelOrderResult CreateFailure(string errorMessage)
        {
            return new CancelOrderResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
