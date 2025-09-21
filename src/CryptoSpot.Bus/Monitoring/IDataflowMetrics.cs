using System;
using CryptoSpot.Bus.Core;

namespace CryptoSpot.Bus.Monitoring
{
    /// <summary>
    /// 数据流监控指标接口
    /// </summary>
    public interface IDataflowMetrics
    {
        /// <summary>
        /// 已处理的命令数量
        /// </summary>
        long ProcessedCommands { get; }
        
        /// <summary>
        /// 失败的命令数量
        /// </summary>
        long FailedCommands { get; }
        
        /// <summary>
        /// 总处理时间
        /// </summary>
        TimeSpan TotalProcessingTime { get; }
        
        /// <summary>
        /// 平均处理时间
        /// </summary>
        TimeSpan AverageProcessingTime { get; }
        
        /// <summary>
        /// 成功率百分比
        /// </summary>
        double SuccessRate { get; }
        
        /// <summary>
        /// 可用并发数
        /// </summary>
        int AvailableConcurrency { get; }
        
        /// <summary>
        /// 最大并发数
        /// </summary>
        int MaxConcurrency { get; }
        
        /// <summary>
        /// 输入队列大小
        /// </summary>
        int InputQueueSize { get; }
    }

    /// <summary>
    /// 支持监控的CommandBus接口
    /// </summary>
    public interface IMonitoredCommandBus : ICommandBus
    {
        /// <summary>
        /// 获取当前监控指标
        /// </summary>
        IDataflowMetrics GetMetrics();
        
        /// <summary>
        /// 重置监控指标
        /// </summary>
        void ResetMetrics();
    }
}
