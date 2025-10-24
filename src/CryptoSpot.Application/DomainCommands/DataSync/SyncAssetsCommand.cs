using CryptoSpot.Bus.Core;

namespace CryptoSpot.Application.DomainCommands.DataSync
{
    /// <summary>
    /// Redis → MySQL 资产同步命令（批量）
    /// </summary>
    public class SyncAssetsCommand : ICommand<SyncAssetsResult>
    {
        /// <summary>
        /// 最大批次大小
        /// </summary>
        public int BatchSize { get; set; } = 500;
        
        /// <summary>
        /// 同步队列的 Redis Key
        /// </summary>
        public string QueueKey { get; set; } = "sync_queue:assets";
    }
    
    /// <summary>
    /// 资产同步结果
    /// </summary>
    public class SyncAssetsResult
    {
        public bool Success { get; set; }
        public int ProcessedCount { get; set; }
        public int FailedCount { get; set; }
        public string? ErrorMessage { get; set; }
        public long ProcessingTimeMs { get; set; }
    }
}
