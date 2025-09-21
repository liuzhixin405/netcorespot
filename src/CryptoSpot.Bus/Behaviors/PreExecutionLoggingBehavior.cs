using System;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Bus.Behaviors
{
    /// <summary>
    /// 方法执行前日志行为
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public class PreExecutionLoggingBehavior<TCommand, TResult> : IPreExecutionBehavior<TCommand, TResult>
    {
        private readonly ILogger<PreExecutionLoggingBehavior<TCommand, TResult>> _logger;

        public PreExecutionLoggingBehavior(ILogger<PreExecutionLoggingBehavior<TCommand, TResult>> logger)
        {
            _logger = logger;
        }

        public async Task<TCommand> BeforeExecutionAsync(TCommand command, CancellationToken ct = default)
        {
            var commandType = typeof(TCommand).Name;
            var timestamp = DateTime.UtcNow;
            
            _logger.LogInformation("🚀 方法执行开始: {CommandType} at {Timestamp}", commandType, timestamp);
            _logger.LogDebug("📝 命令详情: {@Command}", command);
            
            // 可以在这里添加性能监控、权限检查等逻辑
            await Task.CompletedTask;
            
            return command;
        }
    }
}
