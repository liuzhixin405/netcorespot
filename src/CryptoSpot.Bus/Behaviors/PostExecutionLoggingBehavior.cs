using System;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Bus.Behaviors
{
    /// <summary>
    /// 方法执行后日志行为
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public class PostExecutionLoggingBehavior<TCommand, TResult> : IPostExecutionBehavior<TCommand, TResult>
    {
        private readonly ILogger<PostExecutionLoggingBehavior<TCommand, TResult>> _logger;

        public PostExecutionLoggingBehavior(ILogger<PostExecutionLoggingBehavior<TCommand, TResult>> logger)
        {
            _logger = logger;
        }

        public async Task<TResult> AfterExecutionAsync(TCommand command, TResult result, CancellationToken ct = default)
        {
            var commandType = typeof(TCommand).Name;
            var timestamp = DateTime.UtcNow;
            
            _logger.LogInformation("✅ 方法执行完成: {CommandType} at {Timestamp}", commandType, timestamp);
            _logger.LogDebug("📤 执行结果: {@Result}", result);
            
            // 可以在这里添加结果缓存、通知等逻辑
            await Task.CompletedTask;
            
            return result;
        }
    }
}
