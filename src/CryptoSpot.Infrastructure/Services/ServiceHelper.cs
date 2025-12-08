using CryptoSpot.Application.DTOs.Common;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 服务层通用辅助方法，减少重复的 try-catch + ApiResponseDto 模板代码
    /// </summary>
    public static class ServiceHelper
    {
        /// <summary>
        /// 执行异步操作并统一包装为 ApiResponseDto，自动处理异常与日志
        /// </summary>
        /// <typeparam name="T">返回数据类型</typeparam>
        /// <param name="operation">业务逻辑委托</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="errorMessage">失败时的用户友好消息</param>
        /// <param name="errorCode">可选错误码</param>
        /// <param name="context">日志上下文参数</param>
        public static async Task<ApiResponseDto<T>> ExecuteAsync<T>(
            Func<Task<T>> operation,
            ILogger logger,
            string errorMessage,
            string? errorCode = null,
            params object[] context)
        {
            try
            {
                var result = await operation();
                return ApiResponseDto<T>.CreateSuccess(result);
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "参数错误: {Message}", ex.Message);
                return ApiResponseDto<T>.CreateError(ex.Message, "INVALID_ARGUMENT");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, errorMessage + " Context: {@Context}", context);
                return ApiResponseDto<T>.CreateError(errorMessage, errorCode);
            }
        }

        /// <summary>
        /// 执行异步操作并统一包装为 ApiResponseDto（操作本身已返回 ApiResponseDto 时使用）
        /// </summary>
        public static async Task<ApiResponseDto<T>> ExecuteWithResponseAsync<T>(
            Func<Task<ApiResponseDto<T>>> operation,
            ILogger logger,
            string errorMessage,
            string? errorCode = null)
        {
            try
            {
                return await operation();
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "参数错误: {Message}", ex.Message);
                return ApiResponseDto<T>.CreateError(ex.Message, "INVALID_ARGUMENT");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, errorMessage);
                return ApiResponseDto<T>.CreateError(errorMessage, errorCode);
            }
        }

        /// <summary>
        /// 将下游 ApiResponseDto 结果转换/映射为另一种类型
        /// </summary>
        public static ApiResponseDto<TOut> Map<TIn, TOut>(
            ApiResponseDto<TIn> source,
            Func<TIn, TOut> mapper)
        {
            if (!source.Success || source.Data == null)
                return ApiResponseDto<TOut>.CreateError(source.Error ?? "操作失败", source.ErrorCode);
            
            return ApiResponseDto<TOut>.CreateSuccess(mapper(source.Data));
        }

        /// <summary>
        /// 将下游 ApiResponseDto 结果透传（类型相同时）
        /// </summary>
        public static ApiResponseDto<T> Forward<T>(ApiResponseDto<T> source, string? fallbackError = null)
        {
            if (!source.Success)
                return ApiResponseDto<T>.CreateError(source.Error ?? fallbackError ?? "操作失败", source.ErrorCode);
            return source;
        }

        #region 通用工具方法

        /// <summary>
        /// 获取当前 UTC 时间戳（毫秒）
        /// </summary>
        public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>
        /// 生成唯一 ID（前缀 + 时间戳 + 随机数）
        /// </summary>
        /// <param name="prefix">前缀，如 "ORD", "TRD"</param>
        public static string GenerateId(string prefix) 
            => $"{prefix}_{NowMs()}_{Random.Shared.Next(1000, 9999)}";

        /// <summary>
        /// 向下截断到指定精度（不进行四舍五入）
        /// </summary>
        public static decimal RoundDown(decimal value, int precision)
        {
            if (precision < 0) precision = 0;
            var factor = (decimal)Math.Pow(10, precision);
            return Math.Truncate(value * factor) / factor;
        }

        #endregion
    }
}
