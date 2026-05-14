namespace CryptoSpot.Application.Abstractions.Repositories
{
    public interface IUnitOfWork : IDisposable
    {
        IRepository<T> Repository<T>() where T : class;
        Task<int> SaveChangesAsync();
        Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action);
        Task ExecuteInTransactionAsync(Func<Task> action);
    }
}
