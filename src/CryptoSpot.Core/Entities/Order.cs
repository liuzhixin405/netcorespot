using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    public enum OrderSide
    {
        Buy = 1,
        Sell = 2
    }

    public enum OrderType
    {
        Limit = 1,
        Market = 2
    }

    public enum OrderStatus
    {
        Pending = 1,
        PartiallyFilled = 2,
        Filled = 3,
        Cancelled = 4,
        Rejected = 5
    }

    [Table("Orders")]
    public class Order
    {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// 用户ID（用户订单时使用）
        /// </summary>
        public int? UserId { get; set; }

        /// <summary>
        /// 系统账号ID（系统订单时使用）
        /// </summary>
        public int? SystemAccountId { get; set; }

        [Required]
        public int TradingPairId { get; set; }

        [Required]
        [StringLength(50)]
        public string OrderId { get; set; } = string.Empty;

        public OrderSide Side { get; set; }

        public OrderType Type { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 8)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal? Price { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal FilledQuantity { get; set; } = 0;

        [Column(TypeName = "decimal(18, 8)")]
        public decimal AveragePrice { get; set; } = 0;

        public OrderStatus Status { get; set; } = OrderStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        [ForeignKey("SystemAccountId")]
        public virtual SystemAccount? SystemAccount { get; set; }

        [ForeignKey("TradingPairId")]
        public virtual TradingPair TradingPair { get; set; } = null!;

        public virtual ICollection<Trade> Trades { get; set; } = new List<Trade>();

        // Computed properties
        [NotMapped]
        public decimal RemainingQuantity => Quantity - FilledQuantity;

        [NotMapped]
        public decimal TotalValue => (Type == OrderType.Limit && Price.HasValue) ? Quantity * Price.Value : 0;
    }
}
