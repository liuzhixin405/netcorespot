using System;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Bus.Behaviors
{
    /// <summary>
    /// 返回值增强行为 - 返回值贯穿处理
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public class ReturnValueEnhancementBehavior<TCommand, TResult> : IReturnValueInterceptionBehavior<TCommand, TResult>
    {
        private readonly ILogger<ReturnValueEnhancementBehavior<TCommand, TResult>> _logger;

        public ReturnValueEnhancementBehavior(ILogger<ReturnValueEnhancementBehavior<TCommand, TResult>> logger)
        {
            _logger = logger;
        }

        public async Task<TResult> InterceptReturnValueAsync(TCommand command, TResult result, CancellationToken ct = default)
        {
            _logger.LogInformation("🔧 返回值增强处理: {CommandType}", typeof(TCommand).Name);
            
            // 根据返回类型进行不同的增强处理
            if (result is string stringResult)
            {
                // 为字符串结果添加时间戳
                var enhancedResult = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {stringResult}";
                _logger.LogDebug("📝 字符串结果增强: {Original} -> {Enhanced}", stringResult, enhancedResult);
                await Task.CompletedTask;
                return (TResult)(object)enhancedResult;
            }
            else if (result is int intResult)
            {
                // 为整数结果添加处理标识
                var enhancedResult = intResult * 1000; // 示例：放大1000倍
                _logger.LogDebug("📝 整数结果增强: {Original} -> {Enhanced}", intResult, enhancedResult);
                await Task.CompletedTask;
                return (TResult)(object)enhancedResult;
            }
            
            // 其他类型直接返回
            _logger.LogDebug("📝 结果类型无需增强: {ResultType}", typeof(TResult).Name);
            await Task.CompletedTask;
            return result;
        }
    }
}
