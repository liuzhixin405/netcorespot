using System;
using System.Collections.Concurrent;
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
    /// 支持批处理的高性能数据流CommandBus实现
    /// 适用于高吞吐量场景，通过批处理提高效率
    /// </summary>
    public class BatchDataflowCommandBus : ICommandBus, IDisposable
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<BatchDataflowCommandBus>? _logger;
        private readonly ConcurrentDictionary<Type, Func<object>> _handlerCache = new();
        private readonly ConcurrentDictionary<Type, Func<object[]>> _behaviorsCache = new();
        
        // 数据流网络
        private BatchBlock<BatchCommandRequest> _batchBlock = null!;
        private TransformBlock<BatchCommandRequest[], BatchCommandResult[]> _batchProcessor = null!;
        private ActionBlock<BatchCommandResult[]> _resultProcessor = null!;
        
        // 配置参数
        private readonly int _batchSize;
        private readonly TimeSpan _batchTimeout;
        private readonly int _maxConcurrency;
        
        // 监控指标
        private long _processedBatches;
        private long _processedCommands;
        private long _failedCommands;
        private long _totalProcessingTime;
        
        // 批处理超时定时器
        private Timer? _batchTimeoutTimer;

        public BatchDataflowCommandBus(IServiceProvider serviceProvider, ILogger<BatchDataflowCommandBus>? logger = null,
            int batchSize = 10, TimeSpan? batchTimeout = null, int? maxConcurrency = null)
        {
            _provider = serviceProvider;
            _logger = logger;
            _batchSize = batchSize;
            _batchTimeout = batchTimeout ?? TimeSpan.FromMilliseconds(100);
            _maxConcurrency = maxConcurrency ?? Environment.ProcessorCount;
            
            // 创建数据流网络
            CreateDataflowNetwork();
        }

        private void CreateDataflowNetwork()
        {
            // 批处理块
            _batchBlock = new BatchBlock<BatchCommandRequest>(_batchSize, new GroupingDataflowBlockOptions
            {
                BoundedCapacity = _batchSize * 2
            });

            // 批处理器
            _batchProcessor = new TransformBlock<BatchCommandRequest[], BatchCommandResult[]>(
                async batch =>
                {
                    var startTime = DateTime.UtcNow;
                    var results = new BatchCommandResult[batch.Length];
                    
                    _logger?.LogDebug("Processing batch of {Count} commands", batch.Length);
                    
                    // 并行处理批次中的命令
                    var tasks = batch.Select(async (request, index) =>
                    {
                        try
                        {
                            var result = await ProcessCommandPipeline(request);
                            results[index] = new BatchCommandResult(request.Id, result, null);
                            Interlocked.Increment(ref _processedCommands);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Command processing failed for {CommandType}", request.CommandType.Name);
                            results[index] = new BatchCommandResult(request.Id, null, ex);
                            Interlocked.Increment(ref _failedCommands);
                        }
                    });
                    
                    await Task.WhenAll(tasks);
                    
                    var processingTime = DateTime.UtcNow - startTime;
                    Interlocked.Add(ref _totalProcessingTime, processingTime.Ticks);
                    Interlocked.Increment(ref _processedBatches);
                    
                    return results;
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = _maxConcurrency,
                    BoundedCapacity = 2
                });

            // 结果处理器
            _resultProcessor = new ActionBlock<BatchCommandResult[]>(
                results =>
                {
                    foreach (var result in results)
                    {
                        if (result.Exception != null)
                        {
                            result.TaskCompletionSource.SetException(result.Exception);
                        }
                        else
                        {
                            result.TaskCompletionSource.SetResult(result.Result!);
                        }
                    }
                },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 1
                });

            // 连接数据流网络
            _batchBlock.LinkTo(_batchProcessor, new DataflowLinkOptions { PropagateCompletion = true });
            _batchProcessor.LinkTo(_resultProcessor, new DataflowLinkOptions { PropagateCompletion = true });
            
            // 启动批处理超时定时器
            _batchTimeoutTimer = new Timer(TriggerBatchTimeout, null, _batchTimeout, _batchTimeout);
        }
        
        private void TriggerBatchTimeout(object? state)
        {
            try
            {
                // 触发批处理超时，强制处理当前批次
                _batchBlock.TriggerBatch();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error triggering batch timeout");
            }
        }

        public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default) 
            where TCommand : ICommand<TResult>
        {
            var commandType = typeof(TCommand);
            var requestId = Guid.NewGuid();
            var tcs = new TaskCompletionSource<object>();
            
            var request = new BatchCommandRequest(requestId, commandType, typeof(TResult), command, tcs);
            
            // 如果批处理大小为1，直接处理，不使用批处理机制
            if (_batchSize == 1)
            {
                try
                {
                    var result = await ProcessCommandPipeline(request);
                    tcs.SetResult(result);
                    return (TResult)result;
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                    throw;
                }
            }
            
            // 发送到批处理块
            if (!_batchBlock.Post(request))
            {
                throw new InvalidOperationException("Unable to queue command for processing - system may be overloaded");
            }
            
            // 立即触发批处理，确保单个命令也能被处理
            _batchBlock.TriggerBatch();
            
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

        private async Task<object> ProcessCommandPipeline(BatchCommandRequest request)
        {
            // 使用反射调用泛型方法
            var method = typeof(BatchDataflowCommandBus).GetMethod(nameof(ProcessCommandPipelineGeneric), BindingFlags.NonPublic | BindingFlags.Instance);
            var genericMethod = method!.MakeGenericMethod(request.CommandType, request.ResultType);
            
            var task = (Task)genericMethod.Invoke(this, new object[] { request })!;
            await task;
            
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task) ?? throw new InvalidOperationException("Failed to get result from task");
        }

        private async Task<TResult> ProcessCommandPipelineGeneric<TCommand, TResult>(BatchCommandRequest request) 
            where TCommand : ICommand<TResult>
        {
            // 获取处理器和行为的工厂函数
            var handlerFactory = GetCachedHandler<TCommand, TResult>(request.CommandType);
            var behaviorsFactory = GetCachedBehaviors<TCommand, TResult>(request.CommandType);
            
            // 创建处理器和行为的实例
            var handler = handlerFactory();
            var behaviors = behaviorsFactory();
            
            // 构建处理管道
            Func<Task<TResult>> pipeline = () => ExecuteHandler<TCommand, TResult>(handler, (TCommand)request.Command);
            
            // 按顺序应用管道行为
            foreach (var behavior in behaviors.Reverse())
            {
                var currentBehavior = behavior;
                var currentPipeline = pipeline;
                pipeline = async () => (TResult)await ExecuteBehavior(currentBehavior, (TCommand)request.Command, currentPipeline);
            }
            
            return await pipeline();
        }

        private async Task<object> ExecuteBehavior<TCommand, TResult>(
            ICommandPipelineBehavior<TCommand, TResult> behavior, 
            TCommand command, 
            Func<Task<TResult>> next) 
            where TCommand : ICommand<TResult>
        {
            try
            {
                var result = await behavior.Handle(command, cmd => next(), CancellationToken.None);
                return result!;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error executing behavior {behavior.GetType().Name}: {ex.Message}", ex);
            }
        }

        private Func<ICommandHandler<TCommand, TResult>> GetCachedHandler<TCommand, TResult>(Type commandType) 
            where TCommand : ICommand<TResult>
        {
            return (Func<ICommandHandler<TCommand, TResult>>)_handlerCache.GetOrAdd(commandType, _ =>
            {
                return new Func<ICommandHandler<TCommand, TResult>>(() =>
                {
                    using var scope = _provider.CreateScope();
                    var handler = scope.ServiceProvider.GetService<ICommandHandler<TCommand, TResult>>();
                    if (handler == null)
                        throw new InvalidOperationException($"No handler registered for {commandType.Name}");
                    return handler;
                });
            });
        }

        private Func<ICommandPipelineBehavior<TCommand, TResult>[]> GetCachedBehaviors<TCommand, TResult>(Type commandType) 
            where TCommand : ICommand<TResult>
        {
            return (Func<ICommandPipelineBehavior<TCommand, TResult>[]>)_behaviorsCache.GetOrAdd(commandType, _ =>
            {
                return new Func<ICommandPipelineBehavior<TCommand, TResult>[]>(() =>
                {
                    using var scope = _provider.CreateScope();
                    var behaviors = scope.ServiceProvider.GetServices<ICommandPipelineBehavior<TCommand, TResult>>().ToArray();
                    return behaviors;
                });
            });
        }

        private async Task<TResult> ExecuteHandler<TCommand, TResult>(ICommandHandler<TCommand, TResult> handler, TCommand command) 
            where TCommand : ICommand<TResult>
        {
            return await handler.HandleAsync(command, CancellationToken.None);
        }

        private async Task<object> ExecuteHandler(object handler, object command)
        {
            var handlerType = handler.GetType();
            var handleMethod = handlerType.GetMethod("HandleAsync");
            
            if (handleMethod == null)
                throw new InvalidOperationException($"Handler {handlerType.Name} does not have HandleAsync method");

            var task = (Task)handleMethod.Invoke(handler, new object[] { command, CancellationToken.None })!;
            await task;
            
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task) ?? throw new InvalidOperationException("Failed to get result from task");
        }

        private Func<object> GetCachedHandler(Type commandType)
        {
            return _handlerCache.GetOrAdd(commandType, _ =>
            {
                // 获取命令类型实现的ICommand<TResult>接口
                var commandInterface = commandType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
                
                if (commandInterface == null)
                    throw new InvalidOperationException($"Command type {commandType.Name} does not implement ICommand<TResult>");
                
                var resultType = commandInterface.GetGenericArguments()[0];
                var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, resultType);
                
                // 返回一个工厂函数，而不是直接返回处理器实例
                return new Func<object>(() =>
                {
                    using var scope = _provider.CreateScope();
                    var handler = scope.ServiceProvider.GetService(handlerType);
                    if (handler == null)
                        throw new InvalidOperationException($"No handler registered for {commandType.Name}");
                    return handler;
                });
            });
        }

        private Func<object[]> GetCachedBehaviors(Type commandType)
        {
            return _behaviorsCache.GetOrAdd(commandType, _ =>
            {
                // 获取命令类型实现的ICommand<TResult>接口
                var commandInterface = commandType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
                
                if (commandInterface == null)
                    throw new InvalidOperationException($"Command type {commandType.Name} does not implement ICommand<TResult>");
                
                var resultType = commandInterface.GetGenericArguments()[0];
                var behaviorType = typeof(ICommandPipelineBehavior<,>).MakeGenericType(commandType, resultType);
                
                // 返回一个工厂函数，而不是直接返回行为实例
                return new Func<object[]>(() =>
                {
                    using var scope = _provider.CreateScope();
                    var behaviors = scope.ServiceProvider.GetServices(behaviorType).Where(b => b != null).ToArray();
                    return behaviors!;
                });
            });
        }

        // 监控和统计方法
        public BatchDataflowMetrics GetMetrics()
        {
            return new BatchDataflowMetrics
            {
                ProcessedBatches = Interlocked.Read(ref _processedBatches),
                ProcessedCommands = Interlocked.Read(ref _processedCommands),
                FailedCommands = Interlocked.Read(ref _failedCommands),
                TotalProcessingTime = TimeSpan.FromTicks(Interlocked.Read(ref _totalProcessingTime)),
                AverageProcessingTime = _processedCommands > 0 
                    ? TimeSpan.FromTicks(Interlocked.Read(ref _totalProcessingTime) / _processedCommands)
                    : TimeSpan.Zero,
                AverageBatchSize = _processedBatches > 0 
                    ? (double)_processedCommands / _processedBatches 
                    : 0,
                BatchSize = _batchSize,
                BatchTimeout = _batchTimeout,
                InputQueueSize = _batchBlock.OutputCount
            };
        }

        public void ClearCache()
        {
            _handlerCache.Clear();
            _behaviorsCache.Clear();
        }

        public void Dispose()
        {
            _batchTimeoutTimer?.Dispose();
            _batchBlock?.Complete();
            _batchProcessor?.Complete();
            _resultProcessor?.Complete();
        }
    }

    // 辅助类
    internal class BatchCommandRequest
    {
        public Guid Id { get; }
        public Type CommandType { get; }
        public Type ResultType { get; }
        public object Command { get; }
        public TaskCompletionSource<object> TaskCompletionSource { get; }

        public BatchCommandRequest(Guid id, Type commandType, Type resultType, object command, TaskCompletionSource<object> tcs)
        {
            Id = id;
            CommandType = commandType;
            ResultType = resultType;
            Command = command;
            TaskCompletionSource = tcs;
        }
    }

    internal class BatchCommandResult
    {
        public Guid Id { get; }
        public object? Result { get; }
        public Exception? Exception { get; }
        public TaskCompletionSource<object> TaskCompletionSource { get; }

        public BatchCommandResult(Guid id, object? result, Exception? exception)
        {
            Id = id;
            Result = result;
            Exception = exception;
            TaskCompletionSource = new TaskCompletionSource<object>();
        }
    }

}
