using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Bus.Core
{
    /// <summary>
    /// 增强的命令总线，支持完整的AOP横切关注点
    /// </summary>
    public class EnhancedCommandBus : ICommandBus
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly EnhancedPipelineExecutor _pipelineExecutor;
        private readonly ILogger<EnhancedCommandBus> _logger;

        public EnhancedCommandBus(
            IServiceProvider serviceProvider,
            EnhancedPipelineExecutor pipelineExecutor,
            ILogger<EnhancedCommandBus> logger)
        {
            _serviceProvider = serviceProvider;
            _pipelineExecutor = pipelineExecutor;
            _logger = logger;
        }

        /// <summary>
        /// 发送命令并返回结果
        /// </summary>
        /// <typeparam name="TCommand">命令类型</typeparam>
        /// <typeparam name="TResult">返回结果类型</typeparam>
        /// <param name="command">命令对象</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理结果</returns>
        public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default) 
            where TCommand : ICommand<TResult>
        {
            _logger.LogInformation("🚀 增强命令总线开始处理: {CommandType}", typeof(TCommand).Name);

            // 使用增强的管道执行器
            return await _pipelineExecutor.ExecuteAsync<TCommand, TResult>(
                command,
                async (cmd) =>
                {
                    // 获取命令处理器
                    var handler = _serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
                    
                    // 执行命令处理
                    var result = await handler.HandleAsync(cmd, ct);
                    
                    _logger.LogInformation("✅ 命令处理完成: {CommandType}", typeof(TCommand).Name);
                    return result;
                },
                ct);
        }
    }
}
