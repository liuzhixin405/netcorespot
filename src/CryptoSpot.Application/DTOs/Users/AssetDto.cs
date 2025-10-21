using System.ComponentModel.DataAnnotations;

namespace CryptoSpot.Application.DTOs.Users
{
    /// <summary>
    /// 资产数据传输对象
    /// </summary>
    public class AssetDto
    {
        /// <summary>
        /// 资产内部ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        public int? UserId { get; set; }

        /// <summary>
        /// 资产符号
        /// </summary>
        [Required]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 可用余额
        /// </summary>
        [Range(0, double.MaxValue)]
        public decimal Available { get; set; }

        /// <summary>
        /// 冻结余额
        /// </summary>
        [Range(0, double.MaxValue)]
        public decimal Frozen { get; set; }

        /// <summary>
        /// 总余额
        /// </summary>
        public decimal Total { get; set; }

        /// <summary>
        /// 最小保留余额（系统资产）
        /// </summary>
        public decimal MinReserve { get; set; }

        /// <summary>
        /// 目标余额（系统资产）
        /// </summary>
        public decimal TargetBalance { get; set; }

        /// <summary>
        /// 是否启用自动充值（系统资产）
        /// </summary>
        public bool AutoRefillEnabled { get; set; }

        /// <summary>
        /// 可用余额（扣除最小保留）
        /// </summary>
        public decimal UsableBalance { get; set; }

        /// <summary>
        /// 是否为系统资产
        /// </summary>
        public bool IsSystemAsset { get; set; }

        /// <summary>
        /// USDT价值（可选，前端计算使用）
        /// </summary>
        public decimal? UsdtValue { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// 用户资产列表响应DTO
    /// </summary>
    /// <summary>
    /// 资产操作请求DTO
    /// </summary>
    public class AssetOperationRequestDto
    {
        /// <summary>
        /// 资产符号
        /// </summary>
        [Required]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 操作金额
        /// </summary>
        [Range(0.00000001, double.MaxValue)]
        public decimal Amount { get; set; }

        /// <summary>
        /// 操作备注
        /// </summary>
        public string? Remark { get; set; }
    }

    /// <summary>
    /// 资产转账请求DTO
    /// </summary>
    public class AssetTransferRequestDto
    {
        /// <summary>
        /// 接收方用户ID
        /// </summary>
        [Range(1, int.MaxValue)]
        public int ToUserId { get; set; }

        /// <summary>
        /// 资产符号
        /// </summary>
        [Required]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 转账金额
        /// </summary>
        [Range(0.00000001, double.MaxValue)]
        public decimal Amount { get; set; }

        /// <summary>
        /// 转账备注
        /// </summary>
        public string? Remark { get; set; }
    }

    /// <summary>
    /// 资产汇总DTO
    /// </summary>
    public class AssetSummaryDto
    {
        /// <summary>
        /// 总资产价值（USDT）
        /// </summary>
        public decimal TotalValue { get; set; }

        /// <summary>
        /// 可用资产价值（USDT）
        /// </summary>
        public decimal AvailableValue { get; set; }

        /// <summary>
        /// 冻结资产价值（USDT）
        /// </summary>
        public decimal FrozenValue { get; set; }

        /// <summary>
        /// 资产种类数量
        /// </summary>
        public int AssetTypes { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }
}
