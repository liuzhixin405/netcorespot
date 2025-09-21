using System;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Bus.Behaviors
{
    /// <summary>
    /// 异常处理行为
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public class ExceptionHandlingBehavior<TCommand, TResult> : IExceptionHandlingBehavior<TCommand, TResult>
    {
        private readonly ILogger<ExceptionHandlingBehavior<TCommand, TResult>> _logger;

        public ExceptionHandlingBehavior(ILogger<ExceptionHandlingBehavior<TCommand, TResult>> logger)
        {
            _logger = logger;
        }

        public async Task<TResult> HandleExceptionAsync(TCommand command, Exception exception, CancellationToken ct = default)
        {
            var commandType = typeof(TCommand).Name;
            var exceptionType = exception.GetType().Name;
            
            _logger.LogError(exception, "❌ 命令执行异常: {CommandType}, 异常类型: {ExceptionType}", commandType, exceptionType);
            
            // 根据异常类型进行不同的处理
            switch (exception)
            {
                case ArgumentException argEx:
                    _logger.LogWarning("⚠️ 参数异常，返回默认值");
                    await Task.CompletedTask;
                    return GetDefaultResult<TResult>();
                    
                case InvalidOperationException opEx:
                    _logger.LogWarning("⚠️ 操作异常，返回错误信息");
                    if (typeof(TResult) == typeof(string))
                    {
                        await Task.CompletedTask;
                        return (TResult)(object)$"操作失败: {opEx.Message}";
                    }
                    await Task.CompletedTask;
                    return GetDefaultResult<TResult>();
                    
                case TimeoutException timeoutEx:
                    _logger.LogWarning("⚠️ 超时异常，返回超时信息");
                    if (typeof(TResult) == typeof(string))
                    {
                        await Task.CompletedTask;
                        return (TResult)(object)"操作超时，请稍后重试";
                    }
                    await Task.CompletedTask;
                    return GetDefaultResult<TResult>();
                    
                default:
                    _logger.LogError("❌ 未处理的异常类型: {ExceptionType}", exceptionType);
                    throw exception; // 重新抛出未处理的异常
            }
        }
        
        private static TResult GetDefaultResult<TResult>()
        {
            if (typeof(TResult) == typeof(string))
            {
                return (TResult)(object)"处理失败";
            }
            else if (typeof(TResult) == typeof(int))
            {
                return (TResult)(object)(-1);
            }
            else if (typeof(TResult) == typeof(bool))
            {
                return (TResult)(object)false;
            }
            
            return default(TResult)!;
        }
    }
}
