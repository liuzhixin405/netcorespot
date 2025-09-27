namespace CryptoSpot.Application.Abstractions.Repositories
{
    public interface IUnitOfWork : IDisposable
    {
        IRepository<T> Repository<T>() where T : class;
        Task<int> SaveChangesAsync();
        Task<IDbTransaction> BeginTransactionAsync();
        Task CommitTransactionAsync(IDbTransaction transaction);
        Task RollbackTransactionAsync(IDbTransaction transaction);
        Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action);
        Task ExecuteInTransactionAsync(Func<Task> action);
    }

    public interface IDbTransaction : IDisposable
    {
        Task CommitAsync();
        Task RollbackAsync();
    }
}
