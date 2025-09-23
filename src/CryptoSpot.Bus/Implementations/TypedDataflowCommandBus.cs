using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Monitoring;
using System.Collections.Concurrent;

namespace CryptoSpot.Bus.Implementations
{
    /// <summary>
    /// 强类型的TPL数据流CommandBus实现
    /// 避免使用object类型，提供类型安全
    /// </summary>
    public class TypedDataflowCommandBus : ICommandBus, IDisposable
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<TypedDataflowCommandBus>? _logger;

        // 数据流网络
        private ActionBlock<ICommandRequest> _commandProcessor = null!;
        
        // 背压控制
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly int _maxConcurrency;
        
        // 监控指标
        private long _processedCommands;
        private long _failedCommands;
        private long _totalProcessingTime;
        private readonly ConcurrentDictionary<(Type Cmd, Type Res), Type> _processorTypeCache = new();
        private long _totalQueueWaitTicks;

        public TypedDataflowCommandBus(IServiceProvider serviceProvider, ILogger<TypedDataflowCommandBus>? logger = null, 
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
            _commandProcessor = new ActionBlock<ICommandRequest>(
                async request =>
                {
                    var dequeuedAt = DateTime.UtcNow;
                    try
                    {
                        await _concurrencyLimiter.WaitAsync();
                        var startTime = DateTime.UtcNow;
                        Interlocked.Add(ref _totalQueueWaitTicks, (startTime - dequeuedAt).Ticks);
                        
                        // 为当前命令创建scope解析processor（避免缓存跨作用域）
                        using (var scope = _provider.CreateScope())
                        {
                            var processorType = _processorTypeCache.GetOrAdd((request.CommandType, request.ResultType), t =>
                                typeof(CommandProcessor<,>).MakeGenericType(t.Cmd, t.Res));
                            var processor = (ICommandProcessor)scope.ServiceProvider.GetRequiredService(processorType);
                            var cancellable = (request as CancellableCommandRequest)!;
                            await processor.ProcessAsync(request, cancellable.CancellationToken);
                        }
                        
                        var processingTime = DateTime.UtcNow - startTime;
                        Interlocked.Add(ref _totalProcessingTime, processingTime.Ticks);
                        Interlocked.Increment(ref _processedCommands);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _failedCommands);
                        _logger?.LogError(ex, "Command processing failed for {CommandType}", request.CommandType.Name);
                        request.SetException(ex);
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
            var baseRequest = new CommandRequest<TCommand, TResult>(command);
            var request = new CancellableCommandRequest(baseRequest, ct);
            if (!_commandProcessor.Post(request))
                throw new InvalidOperationException("Unable to queue command for processing - system may be overloaded");
            try { var result = await baseRequest.ExecuteAsync(ct); return (TResult)result; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { _logger?.LogWarning("Command {CommandType} was cancelled", typeof(TCommand).Name); throw; }
        }

        // 监控和统计方法
        public DataflowMetrics GetMetrics()
        {
            var processed = Interlocked.Read(ref _processedCommands);
            var failed = Interlocked.Read(ref _failedCommands);
            var totalProcTicks = Interlocked.Read(ref _totalProcessingTime);
            var totalQueueTicks = Interlocked.Read(ref _totalQueueWaitTicks);
            var totalTicks = totalProcTicks + totalQueueTicks;
            var avgProc = processed > 0 ? TimeSpan.FromTicks(totalProcTicks / processed) : TimeSpan.Zero;
            var avgQueue = processed > 0 ? TimeSpan.FromTicks(totalQueueTicks / processed) : TimeSpan.Zero;
            var success = processed + failed > 0 ? (double)processed / (processed + failed) * 100 : 0;
            var failure = processed + failed > 0 ? (double)failed / (processed + failed) * 100 : 0;
            double throughput = 0;
            if (totalTicks > 0)
            {
                var totalSeconds = TimeSpan.FromTicks(totalProcTicks).TotalSeconds;
                if (totalSeconds > 0) throughput = processed / totalSeconds;
            }
            var metrics = new DataflowMetrics
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
                FailureRate = failure
            };
            return metrics;
        }

        public void Dispose()
        {
            _commandProcessor?.Complete();
            _concurrencyLimiter?.Dispose();
        }
    }

    internal sealed class CancellableCommandRequest : ICommandRequest
    {
        private readonly ICommandRequest _inner;
        public CancellationToken CancellationToken { get; }
        public CancellableCommandRequest(ICommandRequest inner, CancellationToken ct)
        { _inner = inner; CancellationToken = ct; }
        public Type CommandType => _inner.CommandType;
        public Type ResultType => _inner.ResultType;
        public Task<object> ExecuteAsync(CancellationToken cancellationToken) => _inner.ExecuteAsync(cancellationToken);
        public void SetResult(object result) => _inner.SetResult(result);
        public void SetException(Exception exception) => _inner.SetException(exception);
    }
}
