using System.ComponentModel.DataAnnotations;

namespace CryptoSpot.Application.DTOs.Trading
{
    /// <summary>
    /// 市场成交数据传输对象 (公开数据,不包含用户信息)
    /// </summary>
    public class MarketTradeDto
    {
        /// <summary>
        /// 交易记录ID
        /// </summary>
        public long Id { get; set; }

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
        /// 成交时间
        /// </summary>
        public DateTime ExecutedAt { get; set; }

        /// <summary>
        /// 是否为买单主动成交 (true=买盘主动成交,价格上涨; false=卖盘主动成交,价格下跌)
        /// </summary>
        public bool IsBuyerMaker { get; set; }
    }
}
