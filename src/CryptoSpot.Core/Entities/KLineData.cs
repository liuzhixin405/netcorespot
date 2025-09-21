using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    [Table("KLineData")]
    public class KLineData
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public int TradingPairId { get; set; }

        [Required]
        [StringLength(10)]
        public string TimeFrame { get; set; } = string.Empty;

        public long OpenTime { get; set; }
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

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("TradingPairId")]
        public virtual TradingPair TradingPair { get; set; } = null!;
    }
}
