using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Persistence.Data;

namespace CryptoSpot.Persistence.Repositories;

public class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly ApplicationDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public BaseRepository(ApplicationDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(int id) => await _dbSet.FindAsync(id);
    public virtual async Task<T?> GetFirstOrDefaultAsync(Expression<Func<T, bool>> predicate) => await _dbSet.FirstOrDefaultAsync(predicate);
    public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate) => await _dbSet.Where(predicate).ToListAsync();
    public virtual async Task<IEnumerable<T>> GetAllAsync() => await _dbSet.ToListAsync();

    public virtual async Task<(IEnumerable<T> Items, int TotalCount)> GetPagedAsync(Expression<Func<T, bool>>? predicate = null, int pageNumber = 1, int pageSize = 10, Expression<Func<T, object>>? orderBy = null, bool isDescending = false)
    {
        var query = _dbSet.AsQueryable();
        if (predicate != null) query = query.Where(predicate);
        var totalCount = await query.CountAsync();
        if (orderBy != null) query = isDescending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, totalCount);
    }

    public virtual async Task<T> AddAsync(T entity) { var entry = await _dbSet.AddAsync(entity); return entry.Entity; }
    public virtual async Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities) { var list = entities.ToList(); await _dbSet.AddRangeAsync(list); return list; }
    public virtual async Task<T> UpdateAsync(T entity) { _dbSet.Update(entity); return await Task.FromResult(entity); }
    public virtual async Task<IEnumerable<T>> UpdateRangeAsync(IEnumerable<T> entities) { var list = entities.ToList(); _dbSet.UpdateRange(list); return await Task.FromResult(list); }
    public virtual async Task<bool> DeleteAsync(T entity) { _dbSet.Remove(entity); return await Task.FromResult(true); }
    public virtual async Task<bool> DeleteByIdAsync(int id) { var entity = await GetByIdAsync(id); if (entity == null) return false; return await DeleteAsync(entity); }
    public virtual async Task<bool> DeleteRangeAsync(IEnumerable<T> entities) { var list = entities.ToList(); _dbSet.RemoveRange(list); return await Task.FromResult(true); }
    public virtual async Task<int> DeleteWhereAsync(Expression<Func<T, bool>> predicate) { var entities = await _dbSet.Where(predicate).ToListAsync(); if (entities.Any()) { _dbSet.RemoveRange(entities); return entities.Count; } return 0; }
    public virtual async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate) => await _dbSet.AnyAsync(predicate);
    public virtual async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null) => predicate == null ? await _dbSet.CountAsync() : await _dbSet.CountAsync(predicate);
}
