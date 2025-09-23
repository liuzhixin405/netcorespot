using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Monitoring;
using CryptoSpot.Bus.Implementations;

namespace CryptoSpot.Bus.Monitoring
{
    /// <summary>
    /// 实时指标收集器接口
    /// </summary>
    public interface IMetricsCollector
    {
        /// <summary>
        /// 获取当前指标
        /// </summary>
        DataflowMetrics GetCurrentMetrics();
        
        /// <summary>
        /// 重置指标
        /// </summary>
        void ResetMetrics();
        
        /// <summary>
        /// 指标更新事件
        /// </summary>
        event EventHandler<MetricsUpdatedEventArgs>? MetricsUpdated;
        
        /// <summary>
        /// 开始收集指标
        /// </summary>
        void StartCollecting();
        
        /// <summary>
        /// 停止收集指标
        /// </summary>
        void StopCollecting();
    }

    /// <summary>
    /// 指标更新事件参数
    /// </summary>
    public class MetricsUpdatedEventArgs : EventArgs
    {
        public DataflowMetrics Metrics { get; }
        public DateTime Timestamp { get; }

        public MetricsUpdatedEventArgs(DataflowMetrics metrics)
        {
            Metrics = metrics;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 实时指标收集器实现
    /// </summary>
    public class MetricsCollector : IMetricsCollector, IDisposable
    {
        private readonly ICommandBus _commandBus;
        private readonly ILogger<MetricsCollector>? _logger;
        private readonly Timer _collectionTimer;
        private readonly TimeSpan _collectionInterval;
        private volatile bool _isCollecting;

        public event EventHandler<MetricsUpdatedEventArgs>? MetricsUpdated;

        public MetricsCollector(ICommandBus commandBus, ILogger<MetricsCollector>? logger = null, 
            TimeSpan? collectionInterval = null)
        {
            _commandBus = commandBus;
            _logger = logger;
            _collectionInterval = collectionInterval ?? TimeSpan.FromSeconds(1);
            _collectionTimer = new Timer(CollectMetrics, null, Timeout.Infinite, Timeout.Infinite);
        }

        public DataflowMetrics GetCurrentMetrics()
        {
            if (_commandBus is TypedDataflowCommandBus typed)
                return typed.GetMetrics();
            if (_commandBus is DataflowCommandBus df)
                return df.GetMetrics();
            if (_commandBus is BatchDataflowCommandBus batch)
            {
                var m = batch.GetMetrics();
                return new DataflowMetrics
                {
                    ProcessedCommands = m.ProcessedCommands,
                    FailedCommands = m.FailedCommands,
                    TotalProcessingTime = m.TotalProcessingTime,
                    AverageProcessingTime = m.AverageProcessingTime,
                    AvailableConcurrency = m.AvailableConcurrency,
                    MaxConcurrency = m.MaxConcurrency,
                    InputQueueSize = m.InputQueueSize,
                    AverageQueueWaitTime = m.AverageQueueWaitTime,
                    TotalQueueWaitTime = m.TotalQueueWaitTime,
                    ThroughputPerSecond = m.ThroughputPerSecond,
                    FailureRate = m.FailureRate
                };
            }
            if (_commandBus is IMonitoredCommandBus monitored)
            {
                var m = monitored.GetMetrics();
                return new DataflowMetrics
                {
                    ProcessedCommands = m.ProcessedCommands,
                    FailedCommands = m.FailedCommands,
                    TotalProcessingTime = m.TotalProcessingTime,
                    AverageProcessingTime = m.AverageProcessingTime,
                    AvailableConcurrency = m.AvailableConcurrency,
                    MaxConcurrency = m.MaxConcurrency,
                    InputQueueSize = m.InputQueueSize,
                    AverageQueueWaitTime = m.AverageQueueWaitTime,
                    TotalQueueWaitTime = m.TotalQueueWaitTime,
                    ThroughputPerSecond = m.ThroughputPerSecond,
                    FailureRate = m.FailureRate
                };
            }
            return new DataflowMetrics();
        }

        public void ResetMetrics()
        {
            switch (_commandBus)
            {
                case IMonitoredCommandBus monitored:
                    monitored.ResetMetrics();
                    break;
                default:
                    // 非监控总线当前仅支持获取累积指标，未实现重置逻辑
                    break;
            }
            
            _logger?.LogInformation("Metrics reset completed");
        }

        public void StartCollecting()
        {
            if (_isCollecting) return;
            
            _isCollecting = true;
            _collectionTimer.Change(TimeSpan.Zero, _collectionInterval);
            _logger?.LogInformation("Started metrics collection with interval {Interval}", _collectionInterval);
        }

        public void StopCollecting()
        {
            if (!_isCollecting) return;
            
            _isCollecting = false;
            _collectionTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _logger?.LogInformation("Stopped metrics collection");
        }

        private void CollectMetrics(object? state)
        {
            if (!_isCollecting) return;

            try
            {
                var metrics = GetCurrentMetrics();
                MetricsUpdated?.Invoke(this, new MetricsUpdatedEventArgs(metrics));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error collecting metrics");
            }
        }

        public void Dispose()
        {
            StopCollecting();
            _collectionTimer?.Dispose();
        }
    }
}
