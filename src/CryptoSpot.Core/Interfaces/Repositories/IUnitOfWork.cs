namespace CryptoSpot.Core.Interfaces.Repositories
{
    /// <summary>
    /// 工作单元接口 - 统一管理事务和仓储
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        /// <summary>
        /// 获取指定类型的仓储
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <returns>仓储实例</returns>
        IRepository<T> Repository<T>() where T : class;

        /// <summary>
        /// 保存所有更改
        /// </summary>
        /// <returns>受影响的行数</returns>
        Task<int> SaveChangesAsync();

        /// <summary>
        /// 开始事务
        /// </summary>
        /// <returns>事务对象</returns>
        Task<IDbTransaction> BeginTransactionAsync();

        /// <summary>
        /// 提交事务
        /// </summary>
        /// <param name="transaction">事务对象</param>
        Task CommitTransactionAsync(IDbTransaction transaction);

        /// <summary>
        /// 回滚事务
        /// </summary>
        /// <param name="transaction">事务对象</param>
        Task RollbackTransactionAsync(IDbTransaction transaction);

        /// <summary>
        /// 执行事务操作
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <returns>操作结果</returns>
        Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action);

        /// <summary>
        /// 执行事务操作（无返回值）
        /// </summary>
        /// <param name="action">要执行的操作</param>
        Task ExecuteInTransactionAsync(Func<Task> action);
    }

    /// <summary>
    /// 数据库事务接口
    /// </summary>
    public interface IDbTransaction : IDisposable
    {
        /// <summary>
        /// 提交事务
        /// </summary>
        Task CommitAsync();

        /// <summary>
        /// 回滚事务
        /// </summary>
        Task RollbackAsync();
    }
}
