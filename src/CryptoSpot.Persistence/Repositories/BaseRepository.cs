using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;

namespace CryptoSpot.Persistence.Repositories;

/// <summary>
/// 基础仓储实现 - 支持独立模式和共享 Context 模式（UnitOfWork）
/// 当 SharedContext 被设置时，仓储方法复用 UnitOfWork 的 DbContext，不再内部调用 SaveChangesAsync
/// </summary>
public class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    internal ApplicationDbContext? SharedContext { get; set; }

    public BaseRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    private async Task<ApplicationDbContext> GetContextAsync()
    {
        if (SharedContext != null) return SharedContext;
        return await _dbContextFactory.CreateDbContextAsync();
    }

    private bool IsSharedContext => SharedContext != null;

    public virtual async Task<T?> GetByIdAsync(long id)
    {
        var context = await GetContextAsync();
        try
        {
            return await context.Set<T>().AsNoTracking().FirstOrDefaultAsync(e => EF.Property<long>(e, "Id") == id);
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<T?> GetFirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        var context = await GetContextAsync();
        try
        {
            return await context.Set<T>().AsNoTracking().FirstOrDefaultAsync(predicate);
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        var context = await GetContextAsync();
        try
        {
            return await context.Set<T>().AsNoTracking().Where(predicate).ToListAsync();
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        var context = await GetContextAsync();
        try
        {
            return await context.Set<T>().AsNoTracking().ToListAsync();
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<(IEnumerable<T> Items, int TotalCount)> GetPagedAsync(Expression<Func<T, bool>>? predicate = null, int pageNumber = 1, int pageSize = 10, Expression<Func<T, object>>? orderBy = null, bool isDescending = false)
    {
        var context = await GetContextAsync();
        try
        {
            var query = context.Set<T>().AsNoTracking().AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            var totalCount = await query.CountAsync();
            if (orderBy != null) query = isDescending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
            var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
            return (items, totalCount);
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        var context = await GetContextAsync();
        try
        {
            var entry = await context.Set<T>().AddAsync(entity);
            if (!IsSharedContext) await context.SaveChangesAsync();
            return entry.Entity;
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities)
    {
        var context = await GetContextAsync();
        try
        {
            var list = entities.ToList();
            await context.Set<T>().AddRangeAsync(list);
            if (!IsSharedContext) await context.SaveChangesAsync();
            return list;
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<T> UpdateAsync(T entity)
    {
        var context = await GetContextAsync();
        try
        {
            context.Set<T>().Update(entity);
            if (!IsSharedContext) await context.SaveChangesAsync();
            return entity;
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<IEnumerable<T>> UpdateRangeAsync(IEnumerable<T> entities)
    {
        var context = await GetContextAsync();
        try
        {
            var list = entities.ToList();
            context.Set<T>().UpdateRange(list);
            if (!IsSharedContext) await context.SaveChangesAsync();
            return list;
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<bool> DeleteAsync(T entity)
    {
        var context = await GetContextAsync();
        try
        {
            context.Set<T>().Remove(entity);
            if (!IsSharedContext) await context.SaveChangesAsync();
            return true;
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<bool> DeleteByIdAsync(int id)
    {
        var context = await GetContextAsync();
        try
        {
            return await context.Set<T>().Where(e => EF.Property<long>(e, "Id") == id).ExecuteDeleteAsync() > 0;
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<bool> DeleteRangeAsync(IEnumerable<T> entities)
    {
        var context = await GetContextAsync();
        try
        {
            var list = entities.ToList();
            context.Set<T>().RemoveRange(list);
            if (!IsSharedContext) await context.SaveChangesAsync();
            return true;
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<int> DeleteWhereAsync(Expression<Func<T, bool>> predicate)
    {
        var context = await GetContextAsync();
        try
        {
            return await context.Set<T>().Where(predicate).ExecuteDeleteAsync();
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
    {
        var context = await GetContextAsync();
        try
        {
            return await context.Set<T>().AsNoTracking().AnyAsync(predicate);
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }

    public virtual async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null)
    {
        var context = await GetContextAsync();
        try
        {
            return predicate == null ? await context.Set<T>().AsNoTracking().CountAsync() : await context.Set<T>().AsNoTracking().CountAsync(predicate);
        }
        finally
        {
            if (!IsSharedContext) await context.DisposeAsync();
        }
    }
}
