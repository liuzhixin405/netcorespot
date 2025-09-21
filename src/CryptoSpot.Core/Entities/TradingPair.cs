using CryptoSpot.Core.Extensions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    [Table("TradingPairs")]
    public class TradingPair : BaseEntity
    {
        [Required]
        [StringLength(20)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        public string BaseAsset { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        public string QuoteAsset { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Price { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Change24h { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Volume24h { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal High24h { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Low24h { get; set; }

        /// <summary>
        /// 最后更新时间戳 (Unix timestamp in milliseconds)
        /// </summary>
        [Column(TypeName = "bigint")]
        public long LastUpdated { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 最小交易数量
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal MinQuantity { get; set; }

        /// <summary>
        /// 最大交易数量
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal MaxQuantity { get; set; }

        /// <summary>
        /// 价格精度（小数点后位数）
        /// </summary>
        public int PricePrecision { get; set; }

        /// <summary>
        /// 数量精度（小数点后位数）
        /// </summary>
        public int QuantityPrecision { get; set; }

        // Navigation properties
        public virtual ICollection<KLineData> KLineData { get; set; } = new List<KLineData>();
        public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
        public virtual ICollection<Trade> Trades { get; set; } = new List<Trade>();

        // Computed properties
        [NotMapped]
        public DateTime LastUpdatedDateTime => DateTimeExtensions.FromUnixTimeMilliseconds(LastUpdated);
    }
}
