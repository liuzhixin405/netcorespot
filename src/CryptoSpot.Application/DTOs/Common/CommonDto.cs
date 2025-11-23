namespace CryptoSpot.Application.DTOs.Common
{
    /// <summary>
    /// 通用API响应DTO（统一所有服务层返回，合并了 OperationResultDto 功能）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class ApiResponseDto<T>
    {
        /// <summary>
        /// 请求是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 响应数据
        /// </summary>
        public T? Data { get; set; }

        /// <summary>
        /// 成功消息
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// 错误代码
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// 详细验证错误信息（字段级错误）
        /// </summary>
        public Dictionary<string, string[]>? ValidationErrors { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 请求ID（用于追踪）
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// 创建成功响应
        /// </summary>
        public static ApiResponseDto<T> CreateSuccess(T? data = default, string? message = null)
        {
            return new ApiResponseDto<T>
            {
                Success = true,
                Data = data,
                Message = message
            };
        }

        /// <summary>
        /// 创建错误响应
        /// </summary>
        public static ApiResponseDto<T> CreateError(string error, string? errorCode = null)
        {
            return new ApiResponseDto<T>
            {
                Success = false,
                Error = error,
                ErrorCode = errorCode
            };
        }

        /// <summary>
        /// 创建失败响应（CreateError 的别名，保持 API 一致性）
        /// </summary>
        public static ApiResponseDto<T> CreateFailure(string error, string? errorCode = null)
        {
            return CreateError(error, errorCode);
        }

        /// <summary>
        /// 创建验证失败响应
        /// </summary>
        public static ApiResponseDto<T> CreateValidationFailure(Dictionary<string, string[]> validationErrors, string? message = "验证失败")
        {
            return new ApiResponseDto<T>
            {
                Success = false,
                Error = message,
                ErrorCode = "VALIDATION_ERROR",
                ValidationErrors = validationErrors
            };
        }
    }

    /// <summary>
    /// 分页响应DTO
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class PagedResponseDto<T>
    {
        /// <summary>
        /// 数据列表
        /// </summary>
        public IEnumerable<T> Items { get; set; } = new List<T>();

        /// <summary>
        /// 总记录数
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 页码
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// 页大小
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        /// <summary>
        /// 是否有下一页
        /// </summary>
        public bool HasNextPage => PageNumber < TotalPages;

        /// <summary>
        /// 是否有上一页
        /// </summary>
        public bool HasPreviousPage => PageNumber > 1;
    }

    /// <summary>
    /// 分页请求DTO
    /// </summary>
    public class PagedRequestDto
    {
        /// <summary>
        /// 页码（从1开始）
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// 页大小
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// 排序字段
        /// </summary>
        public string? SortBy { get; set; }

        /// <summary>
        /// 是否降序
        /// </summary>
        public bool SortDescending { get; set; } = false;

        /// <summary>
        /// 搜索关键词
        /// </summary>
        public string? SearchKeyword { get; set; }
    }

    // ⚠️ 已弃用: OperationResultDto 和 OperationResultDto<T> 已合并到 ApiResponseDto<T>
    // 保留类型别名以保持向后兼容，建议迁移到 ApiResponseDto<T>
    
    /// <summary>
    /// [已弃用] 操作结果DTO - 请使用 ApiResponseDto&lt;object&gt; 替代
    /// 此类型继承自 ApiResponseDto&lt;object&gt;，保持向后兼容
    /// </summary>
    [Obsolete("请使用 ApiResponseDto<object> 替代此类型", false)]
    public class OperationResultDto : ApiResponseDto<object>
    {
    }

    /// <summary>
    /// [已弃用] 操作结果DTO（带返回数据）- 请使用 ApiResponseDto&lt;T&gt; 替代
    /// 此类型继承自 ApiResponseDto&lt;T&gt;，保持向后兼容
    /// </summary>
    [Obsolete("请使用 ApiResponseDto<T> 替代此类型", false)]
    public class OperationResultDto<T> : ApiResponseDto<T>
    {
    }
}
