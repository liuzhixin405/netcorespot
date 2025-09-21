using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    /// <summary>
    /// 系统账号资产
    /// </summary>
    [Table("SystemAssets")]
    public class SystemAsset
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int SystemAccountId { get; set; }

        [Required]
        [StringLength(10)]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 可用余额
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal Available { get; set; }

        /// <summary>
        /// 冻结余额（挂单中）
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal Frozen { get; set; }

        /// <summary>
        /// 最小保留余额
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal MinReserve { get; set; } = 0m;

        /// <summary>
        /// 目标余额（自动充值目标）
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal TargetBalance { get; set; } = 0m;

        /// <summary>
        /// 是否启用自动充值
        /// </summary>
        public bool AutoRefillEnabled { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("SystemAccountId")]
        public virtual SystemAccount SystemAccount { get; set; } = null!;

        // Computed properties
        [NotMapped]
        public decimal Total => Available + Frozen;

        [NotMapped]
        public decimal UsableBalance => Available - MinReserve;
    }
}
