using System.ComponentModel.DataAnnotations;

namespace CryptoSpot.Application.DTOs.Users
{
    /// <summary>
    /// 用户类型枚举
    /// </summary>
    public enum UserTypeDto
    {
        Regular = 1,
        MarketMaker = 2,
        RiskManagement = 3,
        LiquidityProvider = 4,
        Admin = 5
    }

    /// <summary>
    /// 用户数据传输对象
    /// </summary>
    public class UserDto
    {
        /// <summary>
        /// 用户内部ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        [Required]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 邮箱
        /// </summary>
        [EmailAddress]
        public string? Email { get; set; }

        /// <summary>
        /// 用户类型
        /// </summary>
        public UserTypeDto Type { get; set; }

        /// <summary>
        /// 描述信息
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 是否启用自动交易
        /// </summary>
        public bool IsAutoTradingEnabled { get; set; }

        /// <summary>
        /// 最大持仓风险比例
        /// </summary>
        public decimal MaxRiskRatio { get; set; }

        /// <summary>
        /// 每日最大交易金额
        /// </summary>
        public decimal DailyTradingLimit { get; set; }

        /// <summary>
        /// 当日已交易金额
        /// </summary>
        public decimal DailyTradedAmount { get; set; }

        /// <summary>
        /// 是否为系统账号
        /// </summary>
        public bool IsSystemAccount { get; set; }

        /// <summary>
        /// 最后登录时间
        /// </summary>
        public DateTime? LastLoginAt { get; set; }

        /// <summary>
        /// 账号创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 账号更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// 用户简要信息DTO
    /// </summary>
    public class UserSummaryDto
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 用户类型
        /// </summary>
        public UserTypeDto Type { get; set; }

        /// <summary>
        /// 是否为系统账号
        /// </summary>
        public bool IsSystemAccount { get; set; }
    }

    /// <summary>
    /// 用户列表响应DTO
    /// </summary>
    /// <summary>
    /// 创建用户请求DTO
    /// </summary>
    public class CreateUserRequestDto
    {
        /// <summary>
        /// 用户名
        /// </summary>
        [Required]
        [StringLength(50, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 邮箱
        /// </summary>
        [EmailAddress]
        public string? Email { get; set; }

        /// <summary>
        /// 密码（普通用户必填）
        /// </summary>
        [StringLength(255, MinimumLength = 6)]
        public string? Password { get; set; }

        /// <summary>
        /// 用户类型
        /// </summary>
        public UserTypeDto Type { get; set; } = UserTypeDto.Regular;

        /// <summary>
        /// 描述信息
        /// </summary>
        [StringLength(200)]
        public string? Description { get; set; }
    }

    /// <summary>
    /// 更新用户请求DTO
    /// </summary>
    public class UpdateUserRequestDto
    {
        /// <summary>
        /// 邮箱
        /// </summary>
        [EmailAddress]
        public string? Email { get; set; }

        /// <summary>
        /// 描述信息
        /// </summary>
        [StringLength(200)]
        public string? Description { get; set; }

        /// <summary>
        /// 是否启用自动交易
        /// </summary>
        public bool? IsAutoTradingEnabled { get; set; }

        /// <summary>
        /// 最大持仓风险比例
        /// </summary>
        [Range(0, 1)]
        public decimal? MaxRiskRatio { get; set; }

        /// <summary>
        /// 每日最大交易金额
        /// </summary>
        [Range(0, double.MaxValue)]
        public decimal? DailyTradingLimit { get; set; }
    }
}
