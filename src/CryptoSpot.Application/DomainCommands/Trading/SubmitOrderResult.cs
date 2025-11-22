using CryptoSpot.Domain.Entities;

namespace CryptoSpot.Application.DomainCommands.Trading
{
    /// <summary>
    /// 提交订单命令结果 (移动自 Core 层)
    /// </summary>
    public class SubmitOrderResult
    {
        public bool Success { get; set; }
        public long? OrderId { get; set; }
        public string OrderIdString { get; set; } = string.Empty; // 订单ID字符串
        public string ErrorMessage { get; set; } = string.Empty;
        public List<Trade> ExecutedTrades { get; set; } = new();

        public static SubmitOrderResult CreateSuccess(int orderId, string orderIdString)
        {
            return new SubmitOrderResult
            {
                Success = true,
                OrderId = orderId,
                OrderIdString = orderIdString
            };
        }

        public static SubmitOrderResult CreateFailure(string errorMessage)
        {
            return new SubmitOrderResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
