using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    /// <summary>
    /// 统一资产表 - 支持用户资产和系统资产
    /// </summary>
    [Table("Assets")]
    public class Asset : BaseEntity
    {
        /// <summary>
        /// 用户ID（普通用户或系统账号）
        /// </summary>
        public int? UserId { get; set; }

        [Required]
        [StringLength(10)]
        public string Symbol { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Available { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Frozen { get; set; }

        /// <summary>
        /// 最小保留余额（仅系统资产使用）
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal MinReserve { get; set; } = 0m;

        /// <summary>
        /// 目标余额（仅系统资产使用，自动充值目标）
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal TargetBalance { get; set; } = 0m;

        /// <summary>
        /// 是否启用自动充值（仅系统资产使用）
        /// </summary>
        public bool AutoRefillEnabled { get; set; } = false;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        // Computed properties
        [NotMapped]
        public decimal Total => Available + Frozen;

        [NotMapped]
        public decimal UsableBalance => Available - MinReserve;

        [NotMapped]
        public bool IsSystemAsset => UserId.HasValue && User?.IsSystemAccount == true;

        [NotMapped]
        public bool IsUserAsset => UserId.HasValue && User?.IsSystemAccount == false;
    }
}
