using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    [Table("TradingPairs")]
    public class TradingPair
    {
        [Key]
        public int Id { get; set; }

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

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        // Navigation properties
        public virtual ICollection<KLineData> KLineData { get; set; } = new List<KLineData>();
        public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
        public virtual ICollection<Trade> Trades { get; set; } = new List<Trade>();
    }
}
