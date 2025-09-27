using System;
namespace CryptoSpot.Application.Abstractions.Repositories
{
    /// <summary>
    /// 数据库访问协调器，用于防止并发访问问题 (migrated from Core.Interfaces)
    /// </summary>
    public interface IDatabaseCoordinator : IDisposable
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName = "");
        Task ExecuteAsync(Func<Task> operation, string operationName = "");
    }
}
