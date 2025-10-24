using CryptoSpot.Bus.Core;

namespace CryptoSpot.Application.DomainCommands.DataSync
{
    /// <summary>
    /// Redis → MySQL 订单同步命令（批量）
    /// </summary>
    public class SyncOrdersCommand : ICommand<SyncOrdersResult>
    {
        /// <summary>
        /// 最大批次大小
        /// </summary>
        public int BatchSize { get; set; } = 500;
        
        /// <summary>
        /// 同步队列的 Redis Key
        /// </summary>
        public string QueueKey { get; set; } = "sync_queue:orders";
    }
    
    /// <summary>
    /// 订单同步结果
    /// </summary>
    public class SyncOrdersResult
    {
        public bool Success { get; set; }
        public int ProcessedCount { get; set; }
        public int FailedCount { get; set; }
        public string? ErrorMessage { get; set; }
        public long ProcessingTimeMs { get; set; }
    }
}
