using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Domain;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database;

/// <summary>
/// Base repository over a context created per method call. A context never outlives the call
/// that created it and is never stored in a field: its lifetime is the query's lifetime, which
/// is the only lifetime that is actually known here. Writes that must commit together go
/// through <see cref="IDbTransactionRunner{TContext}"/> instead.
/// </summary>
public abstract class EntityFrameworkRepository<TEntity, TKey, TContext>(IDbContextFactory<TContext> contextFactory)
    : IRepository<TEntity, TKey>
    where TEntity : class, IDbEntity<TKey>
    where TContext : DbContext
{
    protected Task<TContext> CreateContextAsync(CancellationToken cancellationToken = default) =>
        contextFactory.CreateDbContextAsync(cancellationToken);

    public async Task<PagedResult<TEntity>> PaginateAsync(EntityPaginateFilter<TEntity> filter, bool track = false,
        CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        IQueryable<TEntity> query = track
            ? context.Set<TEntity>().AsQueryable()
            : context.Set<TEntity>().AsNoTracking().AsQueryable();

        var queryFilters = filter.GetFilter();

        query = query.Where(queryFilters);

        int totalCount = await query.CountAsync(cancellationToken);

        var sortDirection = filter.GetSortDirection();

        var keySelector = filter.GetSortKeySelector();
        if (keySelector is not null)
        {
            query = sortDirection == SortDirection.Ascending
                ? query.OrderBy(keySelector)
                : query.OrderByDescending(keySelector);
        }

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(filter.Page, filter.PageSize, totalCount, items);
    }

    public async Task<List<TEntity>> FindAllAsync(bool track = false, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return track
            ? await context.Set<TEntity>().ToListAsync(cancellationToken)
            : await context.Set<TEntity>().AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<TEntity?> FindByIdAsync(TKey id, bool track = false, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        IQueryable<TEntity> query = track
            ? context.Set<TEntity>()
            : context.Set<TEntity>().AsNoTracking();

        return await query.FirstOrDefaultAsync(
            entity => EF.Property<TKey>(entity, nameof(IDbEntity<TKey>.Id))!.Equals(id), cancellationToken);
    }

    public async Task<List<TEntity>> FindByAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Set<TEntity>()
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public async Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        var entry = context.TrackForInsert(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    public async Task<List<TEntity>> CreateAsync(List<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        var entityList = new List<TEntity>();

        foreach (var entity in entities)
        {
            var entry = context.TrackForInsert(entity);
            entityList.Add(entry.Entity);
        }

        await context.SaveChangesAsync(cancellationToken);

        return entityList;
    }

    public async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        // The context is new, so nothing is tracked and the entity is always detached. The
        // load-then-detach step the shared context needed has no counterpart here.
        var entry = context.TrackForUpdate(entity);

        await context.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    public async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        var entity = await context.Set<TEntity>()
            .FirstOrDefaultAsync(e => EF.Property<TKey>(e, nameof(IDbEntity<TKey>.Id))!.Equals(id), cancellationToken);
        if (entity == null)
        {
            return;
        }

        context.Set<TEntity>().Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }
}
