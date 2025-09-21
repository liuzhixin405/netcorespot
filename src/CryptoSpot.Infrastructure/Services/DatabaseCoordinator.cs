using CryptoSpot.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 数据库访问协调器实现，使用信号量确保数据库操作的线程安全
    /// </summary>
    public class DatabaseCoordinator : IDatabaseCoordinator
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly ILogger<DatabaseCoordinator> _logger;
        private readonly ConcurrentDictionary<string, DateTime> _operationTimestamps = new();

        public DatabaseCoordinator(ILogger<DatabaseCoordinator> logger)
        {
            _logger = logger;
            // 限制同时进行的数据库操作数量为1，确保完全串行化
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName = "")
        {
            await _semaphore.WaitAsync();
            try
            {
                if (!string.IsNullOrEmpty(operationName))
                {
                    _operationTimestamps[operationName] = DateTime.UtcNow;
                    _logger.LogDebug("开始执行数据库操作: {OperationName}", operationName);
                }

                var result = await operation();
                
                if (!string.IsNullOrEmpty(operationName))
                {
                    _logger.LogDebug("完成数据库操作: {OperationName}", operationName);
                }

                return result;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(operationName))
                {
                    _logger.LogError(ex, "数据库操作失败: {OperationName}", operationName);
                }
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task ExecuteAsync(Func<Task> operation, string operationName = "")
        {
            await _semaphore.WaitAsync();
            try
            {
                if (!string.IsNullOrEmpty(operationName))
                {
                    _operationTimestamps[operationName] = DateTime.UtcNow;
                    _logger.LogDebug("开始执行数据库操作: {OperationName}", operationName);
                }

                await operation();
                
                if (!string.IsNullOrEmpty(operationName))
                {
                    _logger.LogDebug("完成数据库操作: {OperationName}", operationName);
                }
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(operationName))
                {
                    _logger.LogError(ex, "数据库操作失败: {OperationName}", operationName);
                }
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }
}
