using System;
using System.Collections.Concurrent;
using System.Linq;
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
    /// 强类型的TPL数据流CommandBus实现
    /// 避免使用object类型，提供类型安全
    /// </summary>
    public class TypedDataflowCommandBus : ICommandBus, IDisposable
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<TypedDataflowCommandBus>? _logger;
        private readonly ConcurrentDictionary<Type, ICommandProcessor> _processorCache = new();
        
        // 数据流网络
        private ActionBlock<ICommandRequest> _commandProcessor = null!;
        
        // 背压控制
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly int _maxConcurrency;
        
        // 监控指标
        private long _processedCommands;
        private long _failedCommands;
        private long _totalProcessingTime;

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
                    try
                    {
                        await _concurrencyLimiter.WaitAsync();
                        var startTime = DateTime.UtcNow;
                        
                        // 获取强类型的处理器
                        var processor = GetCachedProcessor(request.CommandType, request.ResultType);
                        
                        // 执行命令处理
                        await processor.ProcessAsync(request, CancellationToken.None);
                        
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
            var request = new CommandRequest<TCommand, TResult>(command);
            
            // 发送到数据流网络
            if (!_commandProcessor.Post(request))
            {
                throw new InvalidOperationException("Unable to queue command for processing - system may be overloaded");
            }
            
            try
            {
                var result = await request.ExecuteAsync(ct);
                return (TResult)result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger?.LogWarning("Command {CommandType} was cancelled", typeof(TCommand).Name);
                throw;
            }
        }

        private ICommandProcessor GetCachedProcessor(Type commandType, Type resultType)
        {
            return _processorCache.GetOrAdd(commandType, _ =>
            {
                // 创建泛型处理器类型
                var processorType = typeof(CommandProcessor<,>).MakeGenericType(commandType, resultType);
                
                // 创建作用域来解析作用域服务
                using var scope = _provider.CreateScope();
                var processor = (ICommandProcessor)scope.ServiceProvider.GetRequiredService(processorType);
                
                if (processor == null)
                    throw new InvalidOperationException($"No processor registered for {commandType.Name}");
                
                return processor;
            });
        }

        // 监控和统计方法
        public DataflowMetrics GetMetrics()
        {
            return new DataflowMetrics
            {
                ProcessedCommands = Interlocked.Read(ref _processedCommands),
                FailedCommands = Interlocked.Read(ref _failedCommands),
                TotalProcessingTime = TimeSpan.FromTicks(Interlocked.Read(ref _totalProcessingTime)),
                AverageProcessingTime = _processedCommands > 0 
                    ? TimeSpan.FromTicks(Interlocked.Read(ref _totalProcessingTime) / _processedCommands)
                    : TimeSpan.Zero,
                AvailableConcurrency = _concurrencyLimiter.CurrentCount,
                MaxConcurrency = _maxConcurrency,
                InputQueueSize = _commandProcessor.InputCount
            };
        }

        public void ClearCache()
        {
            _processorCache.Clear();
        }

        public void Dispose()
        {
            _commandProcessor?.Complete();
            _concurrencyLimiter?.Dispose();
        }
    }
}
