using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    [Table("Trades")]
    public class Trade
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public long BuyOrderId { get; set; }

        [Required]
        public long SellOrderId { get; set; }

        [Required]
        public int TradingPairId { get; set; }

        [Required]
        [StringLength(50)]
        public string TradeId { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(18, 8)")]
        public decimal Price { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 8)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Fee { get; set; }

        [StringLength(10)]
        public string FeeAsset { get; set; } = string.Empty;

        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("BuyOrderId")]
        public virtual Order BuyOrder { get; set; } = null!;

        [ForeignKey("SellOrderId")]
        public virtual Order SellOrder { get; set; } = null!;

        [ForeignKey("TradingPairId")]
        public virtual TradingPair TradingPair { get; set; } = null!;

        // Computed properties
        [NotMapped]
        public decimal TotalValue => Price * Quantity;
    }
}
