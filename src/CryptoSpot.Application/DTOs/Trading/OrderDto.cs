using System.ComponentModel.DataAnnotations;

namespace CryptoSpot.Application.DTOs.Trading
{
    /// <summary>
    /// 订单状态枚举
    /// </summary>
    public enum OrderStatusDto
    {
        Pending = 1,
        Active = 2,
        PartiallyFilled = 3,
        Filled = 4,
        Cancelled = 5,
        Rejected = 6
    }

    /// <summary>
    /// 订单方向枚举
    /// </summary>
    public enum OrderSideDto
    {
        Buy = 1,
        Sell = 2
    }

    /// <summary>
    /// 订单类型枚举
    /// </summary>
    public enum OrderTypeDto
    {
        Limit = 1,
        Market = 2
    }

    /// <summary>
    /// 订单数据传输对象
    /// </summary>
    public class OrderDto
    {
        /// <summary>
        /// 订单内部ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 业务订单号
        /// </summary>
        public string OrderId { get; set; } = string.Empty;

        /// <summary>
        /// 客户端订单号（可选）
        /// </summary>
        public string? ClientOrderId { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        public int? UserId { get; set; }

        /// <summary>
        /// 交易对符号
        /// </summary>
        [Required]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 订单方向
        /// </summary>
        public OrderSideDto Side { get; set; }

        /// <summary>
        /// 订单类型
        /// </summary>
        public OrderTypeDto Type { get; set; }

        /// <summary>
        /// 订单数量
        /// </summary>
        [Range(0.00000001, double.MaxValue)]
        public decimal Quantity { get; set; }

        /// <summary>
        /// 订单价格（限价单必填）
        /// </summary>
        public decimal? Price { get; set; }

        /// <summary>
        /// 已成交数量
        /// </summary>
        public decimal FilledQuantity { get; set; }

        /// <summary>
        /// 剩余数量
        /// </summary>
        public decimal RemainingQuantity { get; set; }

        /// <summary>
        /// 平均成交价格
        /// </summary>
        public decimal AveragePrice { get; set; }

        /// <summary>
        /// 订单状态
        /// </summary>
        public OrderStatusDto Status { get; set; }

        /// <summary>
        /// 订单创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 订单更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 总价值（限价单）
        /// </summary>
        public decimal TotalValue { get; set; }
    }

    /// <summary>
    /// 创建订单请求DTO
    /// </summary>
    public class CreateOrderRequestDto
    {
        /// <summary>
        /// 交易对符号
        /// </summary>
        [Required]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// 订单方向
        /// </summary>
        public OrderSideDto Side { get; set; }

        /// <summary>
        /// 订单类型
        /// </summary>
        public OrderTypeDto Type { get; set; }

        /// <summary>
        /// 订单数量
        /// </summary>
        [Range(0.00000001, double.MaxValue)]
        public decimal Quantity { get; set; }

        /// <summary>
        /// 订单价格（限价单必填）
        /// </summary>
        public decimal? Price { get; set; }

        /// <summary>
        /// 客户端订单号（可选）
        /// </summary>
        public string? ClientOrderId { get; set; }
    }

    /// <summary>
    /// 订单列表响应DTO
    /// </summary>
    public class OrderListResponseDto
    {
        public IEnumerable<OrderDto> Orders { get; set; } = new List<OrderDto>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }
}
