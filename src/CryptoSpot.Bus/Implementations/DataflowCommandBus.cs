using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Monitoring;

namespace CryptoSpot.Bus.Implementations
{
    /// <summary>
    /// 基于TPL数据流的高性能CommandBus实现
    /// 支持并行处理、背压控制和监控
    /// </summary>
    public class DataflowCommandBus : ICommandBus, IDisposable
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<DataflowCommandBus>? _logger;
        
        // 数据流网络
        private ActionBlock<DataflowCommandRequest> _commandProcessor = null!;
        
        // 背压控制
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly int _maxConcurrency;
        
        // 监控指标
        private long _processedCommands;
        private long _failedCommands;
        private long _totalProcessingTime;
        private long _totalQueueWaitTicks;

        public DataflowCommandBus(IServiceProvider serviceProvider, ILogger<DataflowCommandBus>? logger = null, 
            int? maxConcurrency = null)
        {
            _provider = serviceProvider;
            _logger = logger;
            _maxConcurrency = maxConcurrency ?? Environment.ProcessorCount * 2;
            _concurrencyLimiter = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
            
            // 创建数据流网络
            CreateDataflowNetwork();
        }

        private void CreateDataflowNetwork()
        {
            // 创建命令处理器
            _commandProcessor = new ActionBlock<DataflowCommandRequest>(
                async request =>
                {
                    var dequeuedAt = DateTime.UtcNow;
                    try
                    {
                        await _concurrencyLimiter.WaitAsync();
                        var startTime = DateTime.UtcNow;
                        Interlocked.Add(ref _totalQueueWaitTicks, (startTime - dequeuedAt).Ticks);
                        
                        // 执行完整的命令处理管道
                        var result = await ProcessCommandPipeline(request);
                        
                        var processingTime = DateTime.UtcNow - startTime;
                        Interlocked.Add(ref _totalProcessingTime, processingTime.Ticks);
                        Interlocked.Increment(ref _processedCommands);
                        
                        request.TaskCompletionSource.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _failedCommands);
                        _logger?.LogError(ex, "Command processing failed for {CommandType}", request.CommandType.Name);
                        request.TaskCompletionSource.SetException(ex);
                    }
                    finally
                    {
                        _concurrencyLimiter.Release();
                    }
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = _maxConcurrency,
                    BoundedCapacity = _maxConcurrency * 2
                });
        }

        public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default) 
            where TCommand : ICommand<TResult>
        {
            var commandType = typeof(TCommand);
            var requestId = Guid.NewGuid();
            var tcs = new TaskCompletionSource<object>();
            
            var request = new DataflowCommandRequest(requestId, commandType, typeof(TResult), command, tcs, ct);
            
            // 发送到数据流网络
            if (!_commandProcessor.Post(request))
            {
                throw new InvalidOperationException("Unable to queue command for processing - system may be overloaded");
            }
            
            try
            {
                var result = await tcs.Task.WaitAsync(ct);
                return (TResult)result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger?.LogWarning("Command {CommandType} was cancelled", commandType.Name);
                throw;
            }
        }

        private async Task<object> ProcessCommandPipeline(DataflowCommandRequest request)
        {
            // 使用反射调用泛型方法
            var method = typeof(DataflowCommandBus).GetMethod(nameof(ProcessCommandPipelineGeneric), BindingFlags.NonPublic | BindingFlags.Instance);
            var genericMethod = method!.MakeGenericMethod(request.CommandType, request.ResultType);
            
            var task = (Task)genericMethod.Invoke(this, new object[] { request })!;
            await task;
            
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task) ?? throw new InvalidOperationException("Failed to get result from task");
        }

        private async Task<TResult> ProcessCommandPipelineGeneric<TCommand, TResult>(DataflowCommandRequest request) 
            where TCommand : ICommand<TResult>
        {
            // 每次处理命令创建一个新的Scope，保证Scoped服务（如DbContext）生命周期正确
            using var scope = _provider.CreateScope();
            var serviceProvider = scope.ServiceProvider;

            // 解析处理器 & 管道行为（不缓存具体实例，避免跨Scope持有 DbContext）
            var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
            var behaviors = serviceProvider.GetServices<ICommandPipelineBehavior<TCommand, TResult>>()?.ToArray() 
                           ?? Array.Empty<ICommandPipelineBehavior<TCommand, TResult>>();

            // 构建执行管道（从内向外包装）
            Func<Task<TResult>> pipeline = () => handler.HandleAsync((TCommand)request.Command, request.CancellationToken);

            foreach (var behavior in behaviors.Reverse())
            {
                var next = pipeline;
                var currentBehavior = behavior;
                pipeline = async () => await currentBehavior.Handle((TCommand)request.Command, _ => next(), request.CancellationToken);
            }

            return await pipeline();
        }

        private async Task<object> ExecuteBehavior<TCommand, TResult>(
            ICommandPipelineBehavior<TCommand, TResult> behavior, 
            TCommand command, 
            Func<Task<TResult>> next,
            DataflowCommandRequest request) 
            where TCommand : ICommand<TResult>
        {
            try
            {
                var result = await behavior.Handle(command, cmd => next(), request.CancellationToken);
                return result!;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error executing behavior {behavior.GetType().Name}: {ex.Message}", ex);
            }
        }

        private async Task<TResult> ExecuteHandler<TCommand, TResult>(ICommandHandler<TCommand, TResult> handler, TCommand command, DataflowCommandRequest request) 
            where TCommand : ICommand<TResult>
        {
            return await handler.HandleAsync(command, request.CancellationToken);
        }

        private async Task<object> ExecuteHandler(object handler, object command, DataflowCommandRequest request)
        {
            var handlerType = handler.GetType();
            var handleMethod = handlerType.GetMethod("HandleAsync");
            
            if (handleMethod == null)
                throw new InvalidOperationException($"Handler {handlerType.Name} does not have HandleAsync method");

            var task = (Task)handleMethod.Invoke(handler, new object[] { command, request.CancellationToken })!;
            await task;
            
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task) ?? throw new InvalidOperationException("Failed to get result from task");
        }

        // 监控和统计方法
        public DataflowMetrics GetMetrics()
        {
            var processed = Interlocked.Read(ref _processedCommands);
            var failed = Interlocked.Read(ref _failedCommands);
            var totalProcTicks = Interlocked.Read(ref _totalProcessingTime);
            var totalQueueTicks = Interlocked.Read(ref _totalQueueWaitTicks);
            var avgProc = processed > 0 ? TimeSpan.FromTicks(totalProcTicks / processed) : TimeSpan.Zero;
            var avgQueue = processed > 0 ? TimeSpan.FromTicks(totalQueueTicks / processed) : TimeSpan.Zero;
            var failureRate = processed + failed > 0 ? (double)failed / (processed + failed) * 100 : 0;
            double throughput = 0;
            if (totalProcTicks > 0)
            {
                var totalSeconds = TimeSpan.FromTicks(totalProcTicks).TotalSeconds;
                if (totalSeconds > 0) throughput = processed / totalSeconds;
            }
            return new DataflowMetrics
            {
                ProcessedCommands = processed,
                FailedCommands = failed,
                TotalProcessingTime = TimeSpan.FromTicks(totalProcTicks),
                AverageProcessingTime = avgProc,
                AvailableConcurrency = _concurrencyLimiter.CurrentCount,
                MaxConcurrency = _maxConcurrency,
                InputQueueSize = _commandProcessor.InputCount,
                AverageQueueWaitTime = avgQueue,
                TotalQueueWaitTime = TimeSpan.FromTicks(totalQueueTicks),
                ThroughputPerSecond = throughput,
                FailureRate = failureRate
            };
        }

        public void Dispose()
        {
            _commandProcessor?.Complete();
            _concurrencyLimiter?.Dispose();
        }
    }

    // 辅助类
    internal class DataflowCommandRequest
    {
        public Guid Id { get; }
        public Type CommandType { get; }
        public Type ResultType { get; }
        public object Command { get; }
        public TaskCompletionSource<object> TaskCompletionSource { get; }
        public CancellationToken CancellationToken { get; }

        public DataflowCommandRequest(Guid id, Type commandType, Type resultType, object command, TaskCompletionSource<object> tcs, CancellationToken ct)
        {
            Id = id;
            CommandType = commandType;
            ResultType = resultType;
            Command = command;
            TaskCompletionSource = tcs;
            CancellationToken = ct;
        }
    }

}