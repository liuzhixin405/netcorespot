using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Bus.Core
{
    /// <summary>
    /// 增强的管道执行器，支持完整的AOP横切关注点
    /// </summary>
    public class EnhancedPipelineExecutor
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EnhancedPipelineExecutor> _logger;

        public EnhancedPipelineExecutor(IServiceProvider serviceProvider, ILogger<EnhancedPipelineExecutor> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// 执行增强的管道处理
        /// </summary>
        /// <typeparam name="TCommand">命令类型</typeparam>
        /// <typeparam name="TResult">返回结果类型</typeparam>
        /// <param name="command">命令对象</param>
        /// <param name="handler">最终处理器</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理结果</returns>
        public async Task<TResult> ExecuteAsync<TCommand, TResult>(
            TCommand command, 
            Func<TCommand, Task<TResult>> handler, 
            CancellationToken ct = default)
        {
            try
            {
                // 1. 参数贯穿处理
                var processedCommand = await ProcessParameterInterceptionAsync<TCommand, TResult>(command, ct);
                
                // 2. 方法执行前处理
                var preProcessedCommand = await ProcessPreExecutionAsync<TCommand, TResult>(processedCommand, ct);
                
                // 3. 执行主要逻辑
                var result = await handler(preProcessedCommand);
                
                // 4. 方法执行后处理
                var postProcessedResult = await ProcessPostExecutionAsync<TCommand, TResult>(preProcessedCommand, result, ct);
                
                // 5. 返回值贯穿处理
                var finalResult = await ProcessReturnValueInterceptionAsync<TCommand, TResult>(preProcessedCommand, postProcessedResult, ct);
                
                return finalResult;
            }
            catch (Exception ex)
            {
                // 6. 异常处理
                return await ProcessExceptionHandlingAsync<TCommand, TResult>(command, ex, ct);
            }
        }

        /// <summary>
        /// 处理参数贯穿
        /// </summary>
        private async Task<TCommand> ProcessParameterInterceptionAsync<TCommand, TResult>(TCommand command, CancellationToken ct)
        {
            var behaviors = _serviceProvider.GetServices<IParameterInterceptionBehavior<TCommand, TResult>>();
            
            foreach (var behavior in behaviors)
            {
                try
                {
                    command = await behavior.InterceptParameterAsync(command, ct);
                    _logger?.LogDebug("Parameter interception applied by {BehaviorType}", behavior.GetType().Name);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in parameter interception behavior {BehaviorType}", behavior.GetType().Name);
                    throw;
                }
            }
            
            return command;
        }

        /// <summary>
        /// 处理方法执行前
        /// </summary>
        private async Task<TCommand> ProcessPreExecutionAsync<TCommand, TResult>(TCommand command, CancellationToken ct)
        {
            var behaviors = _serviceProvider.GetServices<IPreExecutionBehavior<TCommand, TResult>>();
            
            foreach (var behavior in behaviors)
            {
                try
                {
                    command = await behavior.BeforeExecutionAsync(command, ct);
                    _logger?.LogDebug("Pre-execution behavior applied by {BehaviorType}", behavior.GetType().Name);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in pre-execution behavior {BehaviorType}", behavior.GetType().Name);
                    throw;
                }
            }
            
            return command;
        }

        /// <summary>
        /// 处理方法执行后
        /// </summary>
        private async Task<TResult> ProcessPostExecutionAsync<TCommand, TResult>(TCommand command, TResult result, CancellationToken ct)
        {
            var behaviors = _serviceProvider.GetServices<IPostExecutionBehavior<TCommand, TResult>>();
            
            foreach (var behavior in behaviors)
            {
                try
                {
                    result = await behavior.AfterExecutionAsync(command, result, ct);
                    _logger?.LogDebug("Post-execution behavior applied by {BehaviorType}", behavior.GetType().Name);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in post-execution behavior {BehaviorType}", behavior.GetType().Name);
                    throw;
                }
            }
            
            return result;
        }

        /// <summary>
        /// 处理返回值贯穿
        /// </summary>
        private async Task<TResult> ProcessReturnValueInterceptionAsync<TCommand, TResult>(TCommand command, TResult result, CancellationToken ct)
        {
            var behaviors = _serviceProvider.GetServices<IReturnValueInterceptionBehavior<TCommand, TResult>>();
            
            foreach (var behavior in behaviors)
            {
                try
                {
                    result = await behavior.InterceptReturnValueAsync(command, result, ct);
                    _logger?.LogDebug("Return value interception applied by {BehaviorType}", behavior.GetType().Name);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in return value interception behavior {BehaviorType}", behavior.GetType().Name);
                    throw;
                }
            }
            
            return result;
        }

        /// <summary>
        /// 处理异常
        /// </summary>
        private async Task<TResult> ProcessExceptionHandlingAsync<TCommand, TResult>(TCommand command, Exception exception, CancellationToken ct)
        {
            var behaviors = _serviceProvider.GetServices<IExceptionHandlingBehavior<TCommand, TResult>>();
            
            foreach (var behavior in behaviors)
            {
                try
                {
                    var result = await behavior.HandleExceptionAsync(command, exception, ct);
                    _logger?.LogDebug("Exception handled by {BehaviorType}", behavior.GetType().Name);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in exception handling behavior {BehaviorType}", behavior.GetType().Name);
                    // 继续尝试下一个异常处理器
                }
            }
            
            // 如果没有异常处理器能够处理，重新抛出原始异常
            _logger?.LogError(exception, "No exception handler could process the exception");
            throw exception;
        }
    }
}
