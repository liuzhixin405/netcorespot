namespace CryptoSpot.Application.DTOs.Trading
{
    /// <summary>
    /// 订单簿深度DTO
    /// </summary>
    public class OrderBookDepthDto
    {
        /// <summary>
        /// 交易对符号
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 买盘（出价）
        /// </summary>
        public List<OrderBookLevelDto> Bids { get; set; } = new();

        /// <summary>
        /// 卖盘（要价）
        /// </summary>
        public List<OrderBookLevelDto> Asks { get; set; } = new();

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 订单簿价格级别DTO
    /// </summary>
    public class OrderBookLevelDto
    {
        /// <summary>
        /// 价格
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>
        /// 数量
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>
        /// 总价值
        /// </summary>
        public decimal Total { get; set; }

        /// <summary>
        /// 订单数量
        /// </summary>
        public int OrderCount { get; set; }
    }

    /// <summary>
    /// 订单匹配结果DTO
    /// </summary>
    public class OrderMatchResultDto
    {
        /// <summary>
        /// 订单信息
        /// </summary>
        public OrderDto Order { get; set; } = null!;

        /// <summary>
        /// 成交记录列表
        /// </summary>
        public List<TradeDto> Trades { get; set; } = new();

        /// <summary>
        /// 是否完全成交
        /// </summary>
        public bool IsFullyMatched { get; set; }

        /// <summary>
        /// 总成交数量
        /// </summary>
        public decimal TotalMatchedQuantity { get; set; }        /// <summary>
        /// 平均成交价格
        /// </summary>
        public decimal AveragePrice { get; set; }
    }

    /// <summary>
    /// 测试订单结果DTO
    /// </summary>
    public class TestOrderResultDto
    {
        /// <summary>
        /// 测试是否通过
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 是否有效（Success 的别名，用于向后兼容）
        /// </summary>
        public bool IsValid => Success;

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 错误消息（如果测试失败）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 预估成交价格
        /// </summary>
        public decimal? EstimatedPrice { get; set; }

        /// <summary>
        /// 预估成交数量
        /// </summary>
        public decimal? EstimatedQuantity { get; set; }

        /// <summary>
        /// 预估手续费
        /// </summary>
        public decimal? EstimatedFee { get; set; }

        /// <summary>
        /// 预估总价值
        /// </summary>
        public decimal? EstimatedTotal { get; set; }

        /// <summary>
        /// 需要的报价资产数量
        /// </summary>
        public decimal? RequiredQuoteAmount { get; set; }

        /// <summary>
        /// 需要的基础资产数量
        /// </summary>
        public decimal? RequiredBaseAmount { get; set; }

        /// <summary>
        /// 风险提示/警告
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// 风险提示（别名，用于向后兼容）
        /// </summary>
        public List<string> RiskWarnings => Warnings;
    }

    /// <summary>
    /// 批量取消订单请求DTO
    /// </summary>
    public class BatchCancelOrdersRequestDto
    {
        /// <summary>
        /// 交易对符号（可选，为空则取消所有交易对的订单）
        /// </summary>
        public string? Symbol { get; set; }

        /// <summary>
        /// 订单状态过滤（可选）
        /// </summary>
        public OrderStatusDto? Status { get; set; }

        /// <summary>
        /// 订单方向过滤（可选）
        /// </summary>
        public OrderSideDto? Side { get; set; }
    }

    /// <summary>
    /// 批量取消订单结果DTO
    /// </summary>
    public class BatchCancelOrdersResultDto
    {
        /// <summary>
        /// 成功取消的订单数量
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 取消失败的订单数量
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 错误消息列表
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// 成功取消的订单ID列表
        /// </summary>
        public List<int> CancelledOrderIds { get; set; } = new();
    }
}
