using CryptoSpot.Core.Extensions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CryptoSpot.Core.ValueObjects;

namespace CryptoSpot.Core.Entities
{
    [Table("Trades")]
    public class Trade : BaseEntity
    {
        [Required]
        public int BuyOrderId { get; set; }

        [Required]
        public int SellOrderId { get; set; }

        [Required]
        public int BuyerId { get; set; }

        [Required]
        public int SellerId { get; set; }

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

        // Value Objects
        [NotMapped]
        public Price TradePrice => new(Price);

        [NotMapped]
        public Quantity TradeQuantity => new(Quantity);

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Fee { get; set; }

        [StringLength(10)]
        public string FeeAsset { get; set; } = string.Empty;

        /// <summary>
        /// 执行时间戳 (Unix timestamp in milliseconds)
        /// </summary>
        [Column(TypeName = "bigint")]
        public long ExecutedAt { get; set; }

        // Navigation properties
        [ForeignKey("TradingPairId")]
        public virtual TradingPair TradingPair { get; set; } = null!;

        // Computed properties
        [NotMapped]
        public decimal TotalValue => Price * Quantity;

        [NotMapped]
        public DateTime ExecutedDateTime => DateTimeExtensions.FromUnixTimeMilliseconds(ExecutedAt);
    }
}
