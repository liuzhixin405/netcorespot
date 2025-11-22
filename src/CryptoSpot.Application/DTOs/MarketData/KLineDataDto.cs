using System.ComponentModel.DataAnnotations;

namespace CryptoSpot.Application.DTOs.MarketData
{
    /// <summary>
    /// K线数据传输对象
    /// </summary>
    public class KLineDataDto
    {
        /// <summary>
        /// K线数据内部ID
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 交易对符号
        /// </summary>
        [Required]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 时间框架
        /// </summary>
        [Required]
        public string TimeFrame { get; set; } = string.Empty;

        /// <summary>
        /// 开盘时间戳（毫秒）
        /// </summary>
        public long OpenTime { get; set; }

        /// <summary>
        /// 收盘时间戳（毫秒）
        /// </summary>
        public long CloseTime { get; set; }

        /// <summary>
        /// 时间戳（兼容性字段，等于OpenTime）
        /// </summary>
        public long Timestamp => OpenTime;

        /// <summary>
        /// 开盘价
        /// </summary>
        [Range(0.00000001, double.MaxValue)]
        public decimal Open { get; set; }

        /// <summary>
        /// 最高价
        /// </summary>
        [Range(0.00000001, double.MaxValue)]
        public decimal High { get; set; }

        /// <summary>
        /// 最低价
        /// </summary>
        [Range(0.00000001, double.MaxValue)]
        public decimal Low { get; set; }

        /// <summary>
        /// 收盘价
        /// </summary>
        [Range(0.00000001, double.MaxValue)]
        public decimal Close { get; set; }

        /// <summary>
        /// 成交量
        /// </summary>
        [Range(0, double.MaxValue)]
        public decimal Volume { get; set; }

        /// <summary>
        /// 开盘时间（DateTime格式）
        /// </summary>
        public DateTime OpenDateTime { get; set; }

        /// <summary>
        /// 收盘时间（DateTime格式）
        /// </summary>
        public DateTime CloseDateTime { get; set; }
    }

    /// <summary>
    /// K线历史数据响应DTO
    /// </summary>
    /// <summary>
    /// K线数据统计DTO
    /// </summary>
    public class KLineDataStatisticsDto
    {
        /// <summary>
        /// 总记录数
        /// </summary>
        public int TotalRecords { get; set; }

        /// <summary>
        /// 第一条记录时间
        /// </summary>
        public DateTime? FirstRecordTime { get; set; }

        /// <summary>
        /// 最后一条记录时间
        /// </summary>
        public DateTime? LastRecordTime { get; set; }

        /// <summary>
        /// 最高价格
        /// </summary>
        public decimal HighestPrice { get; set; }

        /// <summary>
        /// 最低价格
        /// </summary>
        public decimal LowestPrice { get; set; }

        /// <summary>
        /// 总成交量
        /// </summary>
        public decimal TotalVolume { get; set; }
    }
}
