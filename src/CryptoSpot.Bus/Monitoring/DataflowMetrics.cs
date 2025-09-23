using System;

namespace CryptoSpot.Bus.Monitoring
{
    /// <summary>
    /// 数据流指标数据类
    /// </summary>
    public class DataflowMetrics : IDataflowMetrics
    {
        public long ProcessedCommands { get; set; }
        public long FailedCommands { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public int AvailableConcurrency { get; set; }
        public int MaxConcurrency { get; set; }
        public int InputQueueSize { get; set; }
        // 新增指标
        public TimeSpan AverageQueueWaitTime { get; set; }
        public TimeSpan TotalQueueWaitTime { get; set; }
        public double ThroughputPerSecond { get; set; }
        public double FailureRate { get; set; }
        public double SuccessRate => ProcessedCommands + FailedCommands > 0 
            ? (double)ProcessedCommands / (ProcessedCommands + FailedCommands) * 100 
            : 0;
    }
}
