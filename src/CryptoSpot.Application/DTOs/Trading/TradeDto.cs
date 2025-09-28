using System.ComponentModel.DataAnnotations;
using OrderSideDto = CryptoSpot.Domain.Entities.OrderSide; // 统一使用 Domain 枚举

namespace CryptoSpot.Application.DTOs.Trading
{
    /// <summary>
    /// 交易记录数据传输对象
    /// </summary>
    public class TradeDto
    {
        /// <summary>
        /// 交易记录内部ID
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 业务交易号
        /// </summary>
        public string TradeId { get; set; } = string.Empty;

        /// <summary>
        /// 买方订单ID
        /// </summary>
        public int BuyOrderId { get; set; }

        /// <summary>
        /// 卖方订单ID
        /// </summary>
        public int SellOrderId { get; set; }

        /// <summary>
        /// 买方用户ID
        /// </summary>
        public int BuyerId { get; set; }

        /// <summary>
        /// 卖方用户ID
        /// </summary>
        public int SellerId { get; set; }

        /// <summary>
        /// 交易对符号
        /// </summary>
        [Required]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 成交价格
        /// </summary>
        [Range(0.00000001, double.MaxValue)]
        public decimal Price { get; set; }

        /// <summary>
        /// 成交数量
        /// </summary>
        [Range(0.00000001, double.MaxValue)]
        public decimal Quantity { get; set; }

        /// <summary>
        /// 交易手续费
        /// </summary>
        public decimal Fee { get; set; }

        /// <summary>
        /// 手续费资产
        /// </summary>
        public string FeeAsset { get; set; } = string.Empty;

        /// <summary>
        /// 总价值
        /// </summary>
        public decimal TotalValue { get; set; }

        /// <summary>
        /// 执行时间
        /// </summary>
        public DateTime ExecutedAt { get; set; }

        /// <summary>
        /// 交易方向（从用户视角）
        /// </summary>
        public OrderSideDto? Side { get; set; }

        /// <summary>
        /// 兼容旧引用字段
        /// </summary>
        public DateTime ExecutedDateTime => ExecutedAt;
    }

    /// <summary>
    /// 交易历史响应DTO
    /// </summary>
    public class TradeHistoryResponseDto
    {
        public IEnumerable<TradeDto> Trades { get; set; } = new List<TradeDto>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }

    /// <summary>
    /// 用户交易统计DTO
    /// </summary>
    public class TradeStatisticsDto
    {
        /// <summary>
        /// 总交易次数
        /// </summary>
        public int TotalTrades { get; set; }

        /// <summary>
        /// 总交易金额
        /// </summary>
        public decimal TotalVolume { get; set; }

        /// <summary>
        /// 总手续费
        /// </summary>
        public decimal TotalFees { get; set; }

        /// <summary>
        /// 最近交易时间
        /// </summary>
        public DateTime? LastTradeTime { get; set; }

        /// <summary>
        /// 平均交易金额
        /// </summary>
        public decimal AverageTradeSize { get; set; }

        /// <summary>
        /// 总交易价值
        /// </summary>
        public decimal TotalValue { get; set; }

        /// <summary>
        /// 盈亏
        /// </summary>
        public decimal ProfitLoss { get; set; }

        /// <summary>
        /// 胜率
        /// </summary>
        public decimal WinRate { get; set; }

        /// <summary>
        /// 最大盈利
        /// </summary>
        public decimal LargestWin { get; set; }

        /// <summary>
        /// 最大亏损
        /// </summary>
        public decimal LargestLoss { get; set; }
    }
}
