using System;

namespace CryptoSpot.Bus.Monitoring
{
    /// <summary>
    /// 批处理数据流指标数据类
    /// </summary>
    public class BatchDataflowMetrics
    {
        public long ProcessedBatches { get; set; }
        public long ProcessedCommands { get; set; }
        public long FailedCommands { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public double AverageBatchSize { get; set; }
        public int BatchSize { get; set; }
        public TimeSpan BatchTimeout { get; set; }
        public int InputQueueSize { get; set; }
        public int AvailableConcurrency { get; set; } = 0;
        public int MaxConcurrency { get; set; } = 0;
        // 新增字段
        public TimeSpan AverageQueueWaitTime { get; set; }
        public TimeSpan TotalQueueWaitTime { get; set; }
        public double FailureRate { get; set; }
        public double SuccessRate => ProcessedCommands + FailedCommands > 0 
            ? (double)ProcessedCommands / (ProcessedCommands + FailedCommands) * 100 
            : 0;
        /// <summary>
        /// 每秒吞吐量
        /// </summary>
        public double ThroughputPerSecond { get; set; }
    }
}
