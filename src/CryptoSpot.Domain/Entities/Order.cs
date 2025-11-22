using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CryptoSpot.Domain.ValueObjects;

namespace CryptoSpot.Domain.Entities
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
        Active = 2,
        PartiallyFilled = 3,
        Filled = 4,
        Cancelled = 5,
        Rejected = 6
    }

    [Table("Orders")]
    public partial class Order : BaseEntity
    {
        /// <summary>
        /// 用户ID（普通用户或系统账号）
        /// </summary>
        public long? UserId { get; set; }

        [Required]
        public long TradingPairId { get; set; }

        [Required]
        [StringLength(50)]
        public string OrderId { get; set; } = string.Empty;

        [StringLength(50)]
        public string? ClientOrderId { get; set; }

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

        // Value Objects
        [NotMapped]
        public Quantity OrderQuantity => new(Quantity);

        [NotMapped]
        public Price? OrderPrice => Price.HasValue ? new Price(Price.Value) : null;

        [NotMapped]
        public Quantity FilledOrderQuantity => new(FilledQuantity);

        [NotMapped]
        public Price AverageOrderPrice => new(AveragePrice);

        public OrderStatus Status { get; set; } = OrderStatus.Pending;

        // Navigation properties
        [ForeignKey("TradingPairId")]
        public virtual TradingPair TradingPair { get; set; } = null!;

        // Computed properties
        [NotMapped]
        public decimal RemainingQuantity => Quantity - FilledQuantity;

        [NotMapped]
        public decimal TotalValue => (Type == OrderType.Limit && Price.HasValue) ? Quantity * Price.Value : 0;
    }
}
