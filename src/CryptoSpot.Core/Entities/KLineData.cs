using CryptoSpot.Core.Extensions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    [Table("KLineData")]
    public class KLineData : BaseEntity
    {
        [Required]
        public int TradingPairId { get; set; }

        [Required]
        [StringLength(10)]
        public string TimeFrame { get; set; } = string.Empty;

        /// <summary>
        /// 开盘时间戳 (Unix timestamp in milliseconds)
        /// </summary>
        [Column(TypeName = "bigint")]
        public long OpenTime { get; set; }

        /// <summary>
        /// 收盘时间戳 (Unix timestamp in milliseconds)
        /// </summary>
        [Column(TypeName = "bigint")]
        public long CloseTime { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Open { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal High { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Low { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Close { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Volume { get; set; }

        // Navigation properties
        [ForeignKey("TradingPairId")]
        public virtual TradingPair TradingPair { get; set; } = null!;

        // Computed properties
        [NotMapped]
        public DateTime OpenDateTime => DateTimeExtensions.FromUnixTimeMilliseconds(OpenTime);

        [NotMapped]
        public DateTime CloseDateTime => DateTimeExtensions.FromUnixTimeMilliseconds(CloseTime);
    }
}
