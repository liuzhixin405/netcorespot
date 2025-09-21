using CryptoSpot.Core.Extensions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoSpot.Core.Entities
{
    /// <summary>
    /// 用户类型
    /// </summary>
    public enum UserType
    {
        /// <summary>
        /// 普通用户
        /// </summary>
        Regular = 1,
        
        /// <summary>
        /// 做市商账号
        /// </summary>
        MarketMaker = 2,
        
        /// <summary>
        /// 风险管理账号
        /// </summary>
        RiskManagement = 3,
        
        /// <summary>
        /// 流动性提供商账号
        /// </summary>
        LiquidityProvider = 4,
        
        /// <summary>
        /// 系统管理员账号
        /// </summary>
        Admin = 5
    }

    /// <summary>
    /// 统一用户表 - 支持普通用户和系统账号
    /// </summary>
    [Table("Users")]
    public class User : BaseEntity
    {
        [Required]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 邮箱，系统账号可以为空
        /// </summary>
        [EmailAddress]
        [StringLength(100)]
        public string? Email { get; set; }

        /// <summary>
        /// 密码哈希，系统账号可以为空
        /// </summary>
        [StringLength(255)]
        public string? PasswordHash { get; set; }

        /// <summary>
        /// 用户类型
        /// </summary>
        public UserType Type { get; set; } = UserType.Regular;

        /// <summary>
        /// 描述信息（系统账号使用）
        /// </summary>
        [StringLength(200)]
        public string? Description { get; set; }

        /// <summary>
        /// 是否启用自动交易（系统账号使用）
        /// </summary>
        public bool IsAutoTradingEnabled { get; set; } = false;

        /// <summary>
        /// 最大持仓风险比例（系统账号使用）
        /// </summary>
        [Column(TypeName = "decimal(5, 4)")]
        public decimal MaxRiskRatio { get; set; } = 0.1m;

        /// <summary>
        /// 每日最大交易金额（系统账号使用）
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal DailyTradingLimit { get; set; } = 100000m;

        /// <summary>
        /// 当日已交易金额（系统账号使用）
        /// </summary>
        [Column(TypeName = "decimal(18, 8)")]
        public decimal DailyTradedAmount { get; set; } = 0m;

        /// <summary>
        /// 最后登录时间戳 (Unix timestamp in milliseconds)
        /// </summary>
        [Column(TypeName = "bigint")]
        public long? LastLoginAt { get; set; }

        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; } = true;

        // Navigation properties
        public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
        public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
        public virtual ICollection<Trade> Trades { get; set; } = new List<Trade>();

        // Computed properties
        [NotMapped]
        public bool IsSystemAccount => Type != UserType.Regular;

        [NotMapped]
        public bool IsRegularUser => Type == UserType.Regular;

        [NotMapped]
        public DateTime? LastLoginDateTime => LastLoginAt.HasValue ? DateTimeExtensions.FromUnixTimeMilliseconds(LastLoginAt.Value) : null;
    }
}
