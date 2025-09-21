namespace CryptoSpot.Core.Interfaces
{
    /// <summary>
    /// 数据库访问协调器，用于防止并发访问问题
    /// </summary>
    public interface IDatabaseCoordinator
    {
        /// <summary>
        /// 执行数据库操作，确保线程安全
        /// </summary>
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName = "");
        
        /// <summary>
        /// 执行数据库操作，确保线程安全（无返回值）
        /// </summary>
        Task ExecuteAsync(Func<Task> operation, string operationName = "");
    }
}
