namespace CryptoSpot.Domain.Commands.Trading
{
    /// <summary>
    /// 处理K线数据命令结果
    /// </summary>
    public class ProcessKLineDataResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public bool IsNewKLine { get; set; }

        public static ProcessKLineDataResult CreateSuccess(bool isNewKLine = false)
        {
            return new ProcessKLineDataResult
            {
                Success = true,
                IsNewKLine = isNewKLine
            };
        }

        public static ProcessKLineDataResult CreateFailure(string errorMessage)
        {
            return new ProcessKLineDataResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
