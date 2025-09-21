using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Bus.Core
{
    /// <summary>
    /// 强类型的命令处理器接口
    /// </summary>
    public interface ICommandProcessor
    {
        /// <summary>
        /// 处理命令请求
        /// </summary>
        Task ProcessAsync(ICommandRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 泛型命令处理器实现
    /// </summary>
    public class CommandProcessor<TCommand, TResult> : ICommandProcessor
        where TCommand : ICommand<TResult>
    {
        private readonly ICommandHandler<TCommand, TResult> _handler;
        private readonly IEnumerable<ICommandPipelineBehavior<TCommand, TResult>> _behaviors;
        private readonly ILogger<CommandProcessor<TCommand, TResult>>? _logger;

        public CommandProcessor(
            ICommandHandler<TCommand, TResult> handler,
            IEnumerable<ICommandPipelineBehavior<TCommand, TResult>> behaviors,
            ILogger<CommandProcessor<TCommand, TResult>>? logger = null)
        {
            _handler = handler;
            _behaviors = behaviors;
            _logger = logger;
        }

        public async Task ProcessAsync(ICommandRequest request, CancellationToken cancellationToken)
        {
            if (request is not CommandRequest<TCommand, TResult> typedRequest)
            {
                throw new ArgumentException($"Invalid request type. Expected {typeof(CommandRequest<TCommand, TResult>).Name}");
            }

            try
            {
                _logger?.LogDebug("Processing command {CommandType}", typeof(TCommand).Name);
                
                // 构建处理管道
                Func<Task<TResult>> pipeline = () => _handler.HandleAsync(typedRequest.Command, cancellationToken);
                
                // 按顺序应用管道行为
                foreach (var behavior in _behaviors.Reverse())
                {
                    var currentBehavior = behavior;
                    var currentPipeline = pipeline;
                    pipeline = () => currentBehavior.Handle(typedRequest.Command, cmd => currentPipeline(), cancellationToken);
                }
                
                // 执行管道
                var result = await pipeline();
                
                // 设置结果
                typedRequest.SetResult(result!);
                
                _logger?.LogDebug("Command {CommandType} processed successfully", typeof(TCommand).Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing command {CommandType}", typeof(TCommand).Name);
                typedRequest.SetException(ex);
            }
        }
    }
}
