using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CryptoSpot.Persistence.Repositories;

/// <summary>
/// 工作单元 - 维护共享 DbContext，Repository 可通过 SharedContext 属性复用
/// 当 SharedContext 被设置时，Repository 跳过内部 SaveChangesAsync 调用
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ConcurrentDictionary<Type, object> _repositories = new();
    private ApplicationDbContext? _context;
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    public UnitOfWork(IDbContextFactory<ApplicationDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// 延迟获取或创建共享 DbContext
    /// </summary>
    public async Task<ApplicationDbContext> GetOrCreateContextAsync()
    {
        if (_context == null)
        {
            _context = await _dbContextFactory.CreateDbContextAsync();
        }
        return _context;
    }

    public IRepository<T> Repository<T>() where T : class
    {
        var type = typeof(T);
        if (!_repositories.TryGetValue(type, out var repo))
        {
            repo = new BaseRepository<T>(_dbContextFactory);
            _repositories[type] = repo;
        }
        return (IRepository<T>)repo;
    }

    /// <summary>
    /// 保存所有变更（仅在通过 Repository 且 SharedContext 已设置时有效）
    /// </summary>
    public async Task<int> SaveChangesAsync()
    {
        if (_context == null) return 0;
        return await _context.SaveChangesAsync();
    }

    public async Task<IDbTransaction> BeginTransactionAsync()
    {
        var context = await GetOrCreateContextAsync();
        _transaction = await context.Database.BeginTransactionAsync();
        return new DbTransaction(_transaction);
    }

    public async Task CommitTransactionAsync(IDbTransaction transaction)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(IDbTransaction transaction)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action)
    {
        var context = await GetOrCreateContextAsync();
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                var result = await action();
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    public async Task ExecuteInTransactionAsync(Func<Task> action)
    {
        var context = await GetOrCreateContextAsync();
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                await action();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        _transaction?.Dispose();
        _context?.Dispose();
        _repositories.Clear();

        _disposed = true;
    }
}

public class DbTransaction : IDbTransaction
{
    private readonly IDbContextTransaction _transaction;
    public DbTransaction(IDbContextTransaction transaction) => _transaction = transaction;
    public async Task CommitAsync() => await _transaction.CommitAsync();
    public async Task RollbackAsync() => await _transaction.RollbackAsync();
    public void Dispose() => _transaction.Dispose();
}
