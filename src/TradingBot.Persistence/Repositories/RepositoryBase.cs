using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public abstract class RepositoryBase<T> : IRepository<T> where T : class
{
    protected readonly TradingDbContext DbContext;
    private readonly DbSet<T> _dbSet;

    protected RepositoryBase(TradingDbContext dbContext)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _dbSet = dbContext.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync(new object?[] { id }, cancellationToken);
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.AsNoTracking().ToListAsync(cancellationToken);
    }

    public virtual async Task<IEnumerable<T>> GetAsync(ISpecification<T> spec, CancellationToken cancellationToken = default)
    {
        return await ApplySpecification(spec).AsNoTracking().ToListAsync(cancellationToken);
    }

    public virtual async Task<PagedResult<T>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var totalCount = await _dbSet.CountAsync(cancellationToken);
        var items = await _dbSet
            .AsNoTracking()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, totalCount, pageNumber, pageSize);
    }

    public virtual async Task<PagedResult<T>> GetPagedAsync(ISpecification<T> spec, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = ApplySpecification(spec);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .AsNoTracking()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, totalCount, pageNumber, pageSize);
    }

    public virtual async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken);
    }

    public virtual void Update(T entity)
    {
        try
        {
            var entry = DbContext.Entry(entity);
            if (entry.State == EntityState.Detached)
            {
                var keyProperties = DbContext.Model.FindEntityType(typeof(T))?.FindPrimaryKey()?.Properties;
                if (keyProperties != null)
                {
                    var keyValues = keyProperties.Select(p => p.GetGetter().GetClrValue(entity)).ToArray();
                    var trackedEntry = DbContext.ChangeTracker.Entries<T>().FirstOrDefault(e =>
                {
                        var trackedKeyValues = keyProperties.Select(p => p.GetGetter().GetClrValue(e.Entity)).ToArray();
                        return keyValues.SequenceEqual(trackedKeyValues);
                    });

                    if (trackedEntry != null)
                    {
                        trackedEntry.State = EntityState.Detached;
                    }
                }
            }
        }
        catch
        {
            // Fallback if model metadata inspection fails
        }
        _dbSet.Update(entity);
    }

    public virtual void Remove(T entity)
    {
        _dbSet.Remove(entity);
    }

    protected IQueryable<T> ApplySpecification(ISpecification<T> spec)
    {
        var query = _dbSet.AsQueryable();

        if (spec.Criteria != null)
        {
            query = query.Where(spec.Criteria);
        }

        query = spec.Includes.Aggregate(query, (current, include) => current.Include(include));

        if (spec.OrderBy != null)
        {
            query = query.OrderBy(spec.OrderBy);
        }
        else if (spec.OrderByDescending != null)
        {
            query = query.OrderByDescending(spec.OrderByDescending);
        }

        return query;
    }
}
