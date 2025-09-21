using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Monitoring;

namespace CryptoSpot.Bus.Implementations
{
    /// <summary>
    /// 支持监控的标准CommandBus包装器
    /// </summary>
    public class MonitoredCommandBus : IMonitoredCommandBus
    {
        private readonly CommandBus _commandBus;
        private readonly ILogger<MonitoredCommandBus>? _logger;
        
        // 监控指标
        private long _processedCommands;
        private long _failedCommands;
        private long _totalProcessingTime;

        public MonitoredCommandBus(IServiceProvider serviceProvider, ILogger<MonitoredCommandBus>? logger = null)
        {
            _commandBus = new CommandBus(serviceProvider);
            _logger = logger;
        }

        public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default) 
            where TCommand : ICommand<TResult>
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                var result = await _commandBus.SendAsync<TCommand, TResult>(command, ct);
                
                var processingTime = DateTime.UtcNow - startTime;
                Interlocked.Add(ref _totalProcessingTime, processingTime.Ticks);
                Interlocked.Increment(ref _processedCommands);
                
                _logger?.LogDebug("Command {CommandType} processed successfully in {ProcessingTime}ms", 
                    typeof(TCommand).Name, processingTime.TotalMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedCommands);
                _logger?.LogError(ex, "Command {CommandType} failed", typeof(TCommand).Name);
                throw;
            }
        }

        public IDataflowMetrics GetMetrics()
        {
            return new StandardMetrics
            {
                ProcessedCommands = Interlocked.Read(ref _processedCommands),
                FailedCommands = Interlocked.Read(ref _failedCommands),
                TotalProcessingTime = TimeSpan.FromTicks(Interlocked.Read(ref _totalProcessingTime)),
                AverageProcessingTime = _processedCommands > 0 
                    ? TimeSpan.FromTicks(Interlocked.Read(ref _totalProcessingTime) / _processedCommands)
                    : TimeSpan.Zero
            };
        }

        public void ResetMetrics()
        {
            Interlocked.Exchange(ref _processedCommands, 0);
            Interlocked.Exchange(ref _failedCommands, 0);
            Interlocked.Exchange(ref _totalProcessingTime, 0);
        }
    }

    /// <summary>
    /// 支持监控的数据流CommandBus包装器
    /// </summary>
    public class MonitoredDataflowCommandBus : IMonitoredCommandBus
    {
        private readonly DataflowCommandBus _commandBus;
        private readonly ILogger<MonitoredDataflowCommandBus>? _logger;

        public MonitoredDataflowCommandBus(IServiceProvider serviceProvider, ILogger<DataflowCommandBus>? logger = null, 
            int? maxConcurrency = null)
        {
            _commandBus = new DataflowCommandBus(serviceProvider, logger, maxConcurrency);
            _logger = serviceProvider.GetService<ILogger<MonitoredDataflowCommandBus>>();
        }

        public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default) 
            where TCommand : ICommand<TResult>
        {
            return await _commandBus.SendAsync<TCommand, TResult>(command, ct);
        }

        public IDataflowMetrics GetMetrics()
        {
            return (IDataflowMetrics)_commandBus.GetMetrics();
        }

        public void ResetMetrics()
        {
            // 数据流CommandBus的指标是内部管理的，这里可以添加重置逻辑
            _logger?.LogInformation("Metrics reset requested for DataflowCommandBus");
        }

        public void Dispose()
        {
            _commandBus?.Dispose();
        }
    }

    /// <summary>
    /// 支持监控的批处理数据流CommandBus包装器
    /// </summary>
    public class MonitoredBatchDataflowCommandBus : IMonitoredCommandBus
    {
        private readonly BatchDataflowCommandBus _commandBus;
        private readonly ILogger<MonitoredBatchDataflowCommandBus>? _logger;

        public MonitoredBatchDataflowCommandBus(IServiceProvider serviceProvider, ILogger<BatchDataflowCommandBus>? logger = null,
            int batchSize = 10, TimeSpan? batchTimeout = null, int? maxConcurrency = null)
        {
            _commandBus = new BatchDataflowCommandBus(serviceProvider, logger, batchSize, batchTimeout, maxConcurrency);
            _logger = serviceProvider.GetService<ILogger<MonitoredBatchDataflowCommandBus>>();
        }

        public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default) 
            where TCommand : ICommand<TResult>
        {
            return await _commandBus.SendAsync<TCommand, TResult>(command, ct);
        }

        public IDataflowMetrics GetMetrics()
        {
            var batchMetrics = _commandBus.GetMetrics();
            return new BatchMetrics
            {
                ProcessedCommands = batchMetrics.ProcessedCommands,
                FailedCommands = batchMetrics.FailedCommands,
                TotalProcessingTime = batchMetrics.TotalProcessingTime,
                AverageProcessingTime = batchMetrics.AverageProcessingTime,
                ProcessedBatches = batchMetrics.ProcessedBatches,
                AverageBatchSize = batchMetrics.AverageBatchSize,
                Throughput = batchMetrics.Throughput
            };
        }

        public void ResetMetrics()
        {
            // 批处理CommandBus的指标是内部管理的，这里可以添加重置逻辑
            _logger?.LogInformation("Metrics reset requested for BatchDataflowCommandBus");
        }

        public void Dispose()
        {
            _commandBus?.Dispose();
        }
    }

    // 指标实现类
    internal class StandardMetrics : IDataflowMetrics
    {
        public long ProcessedCommands { get; set; }
        public long FailedCommands { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public double SuccessRate => ProcessedCommands + FailedCommands > 0 
            ? (double)ProcessedCommands / (ProcessedCommands + FailedCommands) * 100 
            : 0;
        public int AvailableConcurrency { get; set; } = 0;
        public int MaxConcurrency { get; set; } = 0;
        public int InputQueueSize { get; set; } = 0;
    }

    internal class BatchMetrics : IDataflowMetrics
    {
        public long ProcessedCommands { get; set; }
        public long FailedCommands { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public long ProcessedBatches { get; set; }
        public double AverageBatchSize { get; set; }
        public double Throughput { get; set; }
        public double SuccessRate => ProcessedCommands + FailedCommands > 0 
            ? (double)ProcessedCommands / (ProcessedCommands + FailedCommands) * 100 
            : 0;
        public int AvailableConcurrency { get; set; } = 0;
        public int MaxConcurrency { get; set; } = 0;
        public int InputQueueSize { get; set; } = 0;
    }
}
