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
    /// 支持批处理的高性能数据流CommandBus实现
    /// 适用于高吞吐量场景，通过批处理提高效率
    /// </summary>
    public class BatchDataflowCommandBus : ICommandBus, IDisposable
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<BatchDataflowCommandBus>? _logger;
        
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
        private long _totalQueueWaitTicks;
        
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
                    var dequeuedAt = DateTime.UtcNow;
                    var startTime = DateTime.UtcNow; // 近似：批次整体等待忽略单条差异
                    Interlocked.Add(ref _totalQueueWaitTicks, (startTime - dequeuedAt).Ticks);
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
            
            var request = new BatchCommandRequest(requestId, commandType, typeof(TResult), command, tcs, ct);
            
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
            // 为每个命令建立独立作用域，确保Scoped依赖（DbContext）生命周期正确
            using var scope = _provider.CreateScope();
            var sp = scope.ServiceProvider;

            var handler = sp.GetRequiredService<ICommandHandler<TCommand, TResult>>();
            var behaviors = sp.GetServices<ICommandPipelineBehavior<TCommand, TResult>>()?.ToArray()
                           ?? Array.Empty<ICommandPipelineBehavior<TCommand, TResult>>();

            Func<Task<TResult>> pipeline = () => handler.HandleAsync((TCommand)request.Command, request.CancellationToken);
            foreach (var behavior in behaviors.Reverse())
            {
                var next = pipeline;
                var current = behavior;
                pipeline = () => current.Handle((TCommand)request.Command, _ => next(), request.CancellationToken);
            }
            return await pipeline();
        }

        // 监控和统计方法
        public BatchDataflowMetrics GetMetrics()
        {
            var processedCmds = Interlocked.Read(ref _processedCommands);
            var failed = Interlocked.Read(ref _failedCommands);
            var totalProcTicks = Interlocked.Read(ref _totalProcessingTime);
            var totalQueueTicks = Interlocked.Read(ref _totalQueueWaitTicks);
            var avgProc = processedCmds > 0 ? TimeSpan.FromTicks(totalProcTicks / processedCmds) : TimeSpan.Zero;
            var avgQueue = processedCmds > 0 ? TimeSpan.FromTicks(totalQueueTicks / processedCmds) : TimeSpan.Zero;
            var procSeconds = totalProcTicks > 0 ? TimeSpan.FromTicks(totalProcTicks).TotalSeconds : 0;
            var throughput = procSeconds > 0 ? processedCmds / procSeconds : 0;
            return new BatchDataflowMetrics
            {
                ProcessedBatches = Interlocked.Read(ref _processedBatches),
                ProcessedCommands = processedCmds,
                FailedCommands = failed,
                TotalProcessingTime = TimeSpan.FromTicks(totalProcTicks),
                AverageProcessingTime = avgProc,
                AverageBatchSize = Interlocked.Read(ref _processedBatches) > 0 ? (double)processedCmds / Interlocked.Read(ref _processedBatches) : 0,
                BatchSize = _batchSize,
                BatchTimeout = _batchTimeout,
                InputQueueSize = _batchBlock.OutputCount,
                AverageQueueWaitTime = avgQueue,
                TotalQueueWaitTime = TimeSpan.FromTicks(totalQueueTicks),
                FailureRate = processedCmds + failed > 0 ? (double)failed / (processedCmds + failed) * 100 : 0,
                AvailableConcurrency = 0, // 可按需补充
                MaxConcurrency = _maxConcurrency,
                ThroughputPerSecond = throughput
            };
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
        public CancellationToken CancellationToken { get; }

        public BatchCommandRequest(Guid id, Type commandType, Type resultType, object command, TaskCompletionSource<object> tcs, CancellationToken ct)
        {
            Id = id;
            CommandType = commandType;
            ResultType = resultType;
            Command = command;
            TaskCompletionSource = tcs;
            CancellationToken = ct;
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
