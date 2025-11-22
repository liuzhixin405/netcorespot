namespace CryptoSpot.Application.DTOs.Common
{
    /// <summary>
    /// 通用API响应DTO
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

    /// <summary>
    /// 操作结果DTO
    /// </summary>
    public class OperationResultDto
    {
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// 错误代码
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// 详细错误信息
        /// </summary>
        public Dictionary<string, string[]>? ValidationErrors { get; set; }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        public static OperationResultDto CreateSuccess(string? message = null)
        {
            return new OperationResultDto
            {
                Success = true,
                Message = message
            };
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        public static OperationResultDto CreateFailure(string message, string? errorCode = null)
        {
            return new OperationResultDto
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode
            };
        }

        /// <summary>
        /// 创建验证失败结果
        /// </summary>
        public static OperationResultDto CreateValidationFailure(Dictionary<string, string[]> validationErrors)
        {
            return new OperationResultDto
            {
                Success = false,
                Message = "验证失败",
                ErrorCode = "VALIDATION_ERROR",
                ValidationErrors = validationErrors
            };
        }
    }

    /// <summary>
    /// 操作结果DTO（带返回数据）
    /// </summary>
    /// <typeparam name="T">返回数据类型</typeparam>
    public class OperationResultDto<T> : OperationResultDto
    {
        /// <summary>
        /// 返回数据
        /// </summary>
        public T? Data { get; set; }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        public static OperationResultDto<T> CreateSuccess(T? data = default, string? message = null)
        {
            return new OperationResultDto<T>
            {
                Success = true,
                Data = data,
                Message = message
            };
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        public new static OperationResultDto<T> CreateFailure(string message, string? errorCode = null)
        {
            return new OperationResultDto<T>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode
            };
        }
    }
}
