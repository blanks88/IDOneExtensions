using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Utils.Extensions;

namespace IDOneRepository.Repositories;

public interface IRepository<TEntity> where TEntity : class
{
    Task<TEntity?> GetByIdAsync(object id, CancellationToken ct = default);
    Task<List<TEntity>> GetAllAsync(CancellationToken ct = default);
    IQueryable<TEntity> Query();
    Task<TEntity> AddAsync(TEntity entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default);

    // Upsert operations powered by FlexLabs.EntityFrameworkCore.Upsert
    Task<int> UpsertAsync(
        TEntity entity,
        Expression<Func<TEntity, object>> match,
        Expression<Func<TEntity, TEntity, TEntity>> whenMatched,
        CancellationToken ct = default);

    Task<int> UpsertRangeAsync(
        IEnumerable<TEntity> entities,
        Func<TEntity, string> distinctFn,
        Expression<Func<TEntity, object>> match,
        Expression<Func<TEntity, TEntity, TEntity>> whenMatched,
        CancellationToken ct = default);
}

internal sealed class Repository<TEntity>(DbContext context) : IRepository<TEntity>
    where TEntity : class
{
    private readonly DbSet<TEntity> _dbSet = context.Set<TEntity>();

    public async Task<TEntity?> GetByIdAsync(object id, CancellationToken ct = default)
    {
        var param = Expression.Parameter(typeof(TEntity), "e");
        var prop = Expression.PropertyOrField(param, "Id");
        var equals = Expression.Equal(prop, Expression.Convert(Expression.Constant(id), prop.Type));
        var lambda = Expression.Lambda<Func<TEntity, bool>>(equals, param);
        return await _dbSet.FirstOrDefaultAsync(lambda, ct);
    }

    public async Task<List<TEntity>> GetAllAsync(CancellationToken ct = default)
        => await _dbSet.ToListAsync(ct);

    public IQueryable<TEntity> Query() => _dbSet.AsQueryable();

    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken ct = default)
    {
        _dbSet.Add(entity);
        return await Task.FromResult(entity);
    }

    public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default)
    {
        _dbSet.AddRange(entities);
        await Task.CompletedTask;
    }

    public void Update(TEntity entity) => _dbSet.Update(entity);

    public void Remove(TEntity entity) => _dbSet.Remove(entity);

    public Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default)
        => predicate == null ? _dbSet.CountAsync(ct) : _dbSet.CountAsync(predicate, ct);

    public Task<int> UpsertAsync(
        TEntity entity,
        Expression<Func<TEntity, object>> match,
        Expression<Func<TEntity, TEntity, TEntity>> whenMatched,
        CancellationToken ct = default)
    {
        return _dbSet.Upsert(entity)
            .On(match)
            .WhenMatched(whenMatched)
            .RunAsync(ct);
    }

    public Task<int> UpsertRangeAsync(
        IEnumerable<TEntity> entities,
        Func<TEntity, string> distinctFn,
        Expression<Func<TEntity, object>> match,
        Expression<Func<TEntity, TEntity, TEntity>> whenMatched,
        CancellationToken ct = default)
    {
        var src = entities.RemoveDuplicated(distinctFn);
        
        return _dbSet.UpsertRange(src)
            .On(match)
            .WhenMatched(whenMatched)
            .RunAsync(ct);
    }
}