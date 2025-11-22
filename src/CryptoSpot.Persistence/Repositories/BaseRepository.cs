using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;

namespace CryptoSpot.Persistence.Repositories;

/// <summary>
/// 基础仓储实现 - 使用 IDbContextFactory 按需创建 DbContext
/// 优势：线程安全、支持长生命周期场景（BackgroundService）、自动资源管理
/// </summary>
public class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

    public BaseRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public virtual async Task<T?> GetByIdAsync(long id)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<T>().FindAsync(id);
    }

    public virtual async Task<T?> GetFirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<T>().FirstOrDefaultAsync(predicate);
    }

    public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<T>().Where(predicate).ToListAsync();
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<T>().ToListAsync();
    }

    public virtual async Task<(IEnumerable<T> Items, int TotalCount)> GetPagedAsync(Expression<Func<T, bool>>? predicate = null, int pageNumber = 1, int pageSize = 10, Expression<Func<T, object>>? orderBy = null, bool isDescending = false)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var query = context.Set<T>().AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        var totalCount = await query.CountAsync();
        if (orderBy != null) query = isDescending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, totalCount);
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var entry = await context.Set<T>().AddAsync(entity);
        await context.SaveChangesAsync();
        return entry.Entity;
    }

    public virtual async Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var list = entities.ToList();
        await context.Set<T>().AddRangeAsync(list);
        await context.SaveChangesAsync();
        return list;
    }

    public virtual async Task<T> UpdateAsync(T entity)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        context.Set<T>().Update(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public virtual async Task<IEnumerable<T>> UpdateRangeAsync(IEnumerable<T> entities)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var list = entities.ToList();
        context.Set<T>().UpdateRange(list);
        await context.SaveChangesAsync();
        return list;
    }

    public virtual async Task<bool> DeleteAsync(T entity)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        context.Set<T>().Remove(entity);
        await context.SaveChangesAsync();
        return true;
    }

    public virtual async Task<bool> DeleteByIdAsync(int id)
    {
        var entity = await GetByIdAsync(id);
        if (entity == null) return false;
        return await DeleteAsync(entity);
    }

    public virtual async Task<bool> DeleteRangeAsync(IEnumerable<T> entities)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var list = entities.ToList();
        context.Set<T>().RemoveRange(list);
        await context.SaveChangesAsync();
        return true;
    }

    public virtual async Task<int> DeleteWhereAsync(Expression<Func<T, bool>> predicate)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var entities = await context.Set<T>().Where(predicate).ToListAsync();
        if (entities.Any())
        {
            context.Set<T>().RemoveRange(entities);
            await context.SaveChangesAsync();
            return entities.Count;
        }
        return 0;
    }

    public virtual async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return await context.Set<T>().AnyAsync(predicate);
    }

    public virtual async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        return predicate == null ? await context.Set<T>().CountAsync() : await context.Set<T>().CountAsync(predicate);
    }
}
