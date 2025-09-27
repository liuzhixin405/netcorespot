using System.ComponentModel.DataAnnotations;

namespace CryptoSpot.Application.DTOs.Trading
{
    /// <summary>
    /// 交易对数据传输对象
    /// </summary>
    public class TradingPairDto
    {
        /// <summary>
        /// 交易对内部ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 交易对符号
        /// </summary>
        [Required]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 基础资产
        /// </summary>
        [Required]
        public string BaseAsset { get; set; } = string.Empty;

        /// <summary>
        /// 计价资产
        /// </summary>
        [Required]
        public string QuoteAsset { get; set; } = string.Empty;

        /// <summary>
        /// 当前价格
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>
        /// 24小时价格变化
        /// </summary>
        public decimal Change24h { get; set; }

        /// <summary>
        /// 24小时价格变化百分比
        /// </summary>
        public decimal Change24hPercent { get; set; }

        /// <summary>
        /// 24小时交易量
        /// </summary>
        public decimal Volume24h { get; set; }

        /// <summary>
        /// 24小时最高价
        /// </summary>
        public decimal High24h { get; set; }

        /// <summary>
        /// 24小时最低价
        /// </summary>
        public decimal Low24h { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 最小交易数量
        /// </summary>
        public decimal MinQuantity { get; set; }

        /// <summary>
        /// 最大交易数量
        /// </summary>
        public decimal MaxQuantity { get; set; }

        /// <summary>
        /// 价格精度（小数点后位数）
        /// </summary>
        public int PricePrecision { get; set; }

        /// <summary>
        /// 数量精度（小数点后位数）
        /// </summary>
        public int QuantityPrecision { get; set; }
    }

    /// <summary>
    /// 交易对列表响应DTO
    /// </summary>
    public class TradingPairListResponseDto
    {
        public IEnumerable<TradingPairDto> TradingPairs { get; set; } = new List<TradingPairDto>();
        public int TotalCount { get; set; }
    }

    /// <summary>
    /// 交易对简要信息DTO（用于下拉列表等）
    /// </summary>
    public class TradingPairSummaryDto
    {
        /// <summary>
        /// 交易对符号
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 基础资产
        /// </summary>
        public string BaseAsset { get; set; } = string.Empty;

        /// <summary>
        /// 计价资产
        /// </summary>
        public string QuoteAsset { get; set; } = string.Empty;

        /// <summary>
        /// 当前价格
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>
        /// 24小时价格变化百分比
        /// </summary>
        public decimal Change24hPercent { get; set; }

        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; }
    }
}
