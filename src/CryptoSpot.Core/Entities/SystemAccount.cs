using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    /// <summary>
    /// 系统交易账号类型
    /// </summary>
    public enum SystemAccountType
    {
        /// <summary>
        /// 做市商账号 - 提供流动性
        /// </summary>
        MarketMaker = 1,
        
        /// <summary>
        /// 风险管理账号 - 对冲风险
        /// </summary>
        RiskManagement = 2,
        
        /// <summary>
        /// 流动性提供商账号
        /// </summary>
        LiquidityProvider = 3
    }

    /// <summary>
    /// 系统交易账号
    /// </summary>
    [Table("SystemAccounts")]
    public class SystemAccount
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        public SystemAccountType Type { get; set; }

        /// <summary>
        /// 是否启用自动交易
        /// </summary>
        public bool IsAutoTradingEnabled { get; set; } = true;

        /// <summary>
        /// 最大持仓风险比例
        /// </summary>
        [Column(TypeName = "decimal(5, 4)")]
        public decimal MaxRiskRatio { get; set; } = 0.1m;

        /// <summary>
        /// 每日最大交易金额
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal DailyTradingLimit { get; set; } = 100000m;

        /// <summary>
        /// 当日已交易金额
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal DailyTradedAmount { get; set; } = 0m;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<SystemAsset> Assets { get; set; } = new List<SystemAsset>();
        public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}
