using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;

namespace CryptoSpot.Persistence.Repositories;

public class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly ApplicationDbContext _dbContext;

    public BaseRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public virtual async Task<T?> GetByIdAsync(long id)
    {
        return await _dbContext.Set<T>().AsNoTracking()
            .FirstOrDefaultAsync(e => EF.Property<long>(e, "Id") == id);
    }

    public virtual async Task<T?> GetFirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        return await _dbContext.Set<T>().AsNoTracking().FirstOrDefaultAsync(predicate);
    }

    public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return await _dbContext.Set<T>().AsNoTracking().Where(predicate).ToListAsync();
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _dbContext.Set<T>().AsNoTracking().ToListAsync();
    }

    public virtual async Task<(IEnumerable<T> Items, int TotalCount)> GetPagedAsync(
        Expression<Func<T, bool>>? predicate = null,
        int pageNumber = 1,
        int pageSize = 10,
        Expression<Func<T, object>>? orderBy = null,
        bool isDescending = false)
    {
        var query = _dbContext.Set<T>().AsNoTracking().AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        var totalCount = await query.CountAsync();
        if (orderBy != null) query = isDescending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, totalCount);
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        var entry = await _dbContext.Set<T>().AddAsync(entity);
        await _dbContext.SaveChangesAsync();
        return entry.Entity;
    }

    public virtual async Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities)
    {
        var list = entities.ToList();
        await _dbContext.Set<T>().AddRangeAsync(list);
        await _dbContext.SaveChangesAsync();
        return list;
    }

    public virtual async Task<T> UpdateAsync(T entity)
    {
        _dbContext.Set<T>().Update(entity);
        await _dbContext.SaveChangesAsync();
        return entity;
    }

    public virtual async Task<IEnumerable<T>> UpdateRangeAsync(IEnumerable<T> entities)
    {
        var list = entities.ToList();
        _dbContext.Set<T>().UpdateRange(list);
        await _dbContext.SaveChangesAsync();
        return list;
    }

    public virtual async Task<bool> DeleteAsync(T entity)
    {
        _dbContext.Set<T>().Remove(entity);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public virtual async Task<bool> DeleteByIdAsync(int id)
    {
        return await _dbContext.Set<T>()
            .Where(e => EF.Property<long>(e, "Id") == id)
            .ExecuteDeleteAsync() > 0;
    }

    public virtual async Task<bool> DeleteRangeAsync(IEnumerable<T> entities)
    {
        var list = entities.ToList();
        _dbContext.Set<T>().RemoveRange(list);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public virtual async Task<int> DeleteWhereAsync(Expression<Func<T, bool>> predicate)
    {
        return await _dbContext.Set<T>().Where(predicate).ExecuteDeleteAsync();
    }

    public virtual async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate)
    {
        return await _dbContext.Set<T>().AsNoTracking().AnyAsync(predicate);
    }

    public virtual async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null)
    {
        return predicate == null
            ? await _dbContext.Set<T>().AsNoTracking().CountAsync()
            : await _dbContext.Set<T>().AsNoTracking().CountAsync(predicate);
    }
}
