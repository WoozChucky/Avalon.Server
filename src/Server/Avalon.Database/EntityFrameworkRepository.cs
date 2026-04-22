using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Domain;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database;

public abstract class EntityFrameworkRepository<TEntity, TKey>(DbContext dbContext) : IRepository<TEntity, TKey>
    where TEntity : class, IDbEntity<TKey>
{
    protected readonly DbContext Context = dbContext;

    public async Task<PagedResult<TEntity>> PaginateAsync(EntityPaginateFilter<TEntity> filter, bool track = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> query = track
            ? Context.Set<TEntity>().AsQueryable()
            : Context.Set<TEntity>().AsNoTracking().AsQueryable();

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
        return track
            ? await Context.Set<TEntity>().ToListAsync(cancellationToken)
            : await FindAllNoTrackingAsync(cancellationToken);
    }

    private async Task<List<TEntity>> FindAllNoTrackingAsync(CancellationToken cancellationToken = default)
    {
        return await Context.Set<TEntity>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<TEntity?> FindByIdAsync(TKey id, bool track = false, CancellationToken cancellationToken = default)
    {
        return track
            ? await Context.Set<TEntity>()
                .FirstOrDefaultAsync(entity => EF.Property<TKey>(entity, nameof(IDbEntity<>.Id))!.Equals(id), cancellationToken)
            : await FindByIdNoTrackingAsync(id, cancellationToken);
    }

    private async Task<TEntity?> FindByIdNoTrackingAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return await Context.Set<TEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => EF.Property<TKey>(entity, nameof(IDbEntity<TKey>.Id))!.Equals(id), cancellationToken);
    }

    public async Task<List<TEntity>> FindByAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await Context.Set<TEntity>()
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public async Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        var entry = await Context.Set<TEntity>().AddAsync(entity, cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    public async Task<List<TEntity>> CreateAsync(List<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var entityList = new List<TEntity>();

        foreach (var entity in entities)
        {
            var entry = await Context.Set<TEntity>().AddAsync(entity, cancellationToken);
            entityList.Add(entry.Entity);
        }

        await Context.SaveChangesAsync(cancellationToken);

        return entityList;
    }

    public async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // Detach existing entity if tracked
        var existingEntity = await Context.Set<TEntity>().FindAsync([entity.Id], cancellationToken);
        if (existingEntity != null)
        {
            Context.Entry(existingEntity).State = EntityState.Detached;
        }

        // Attach and set state to modified
        var entry = Context.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            Context.Set<TEntity>().Attach(entity);
        }
        entry.State = EntityState.Modified;

        await Context.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    public async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await FindByIdAsync(id, cancellationToken: cancellationToken);
        if (entity == null)
        {
            return;
        }

        Context.Set<TEntity>().Remove(entity);
        await Context.SaveChangesAsync(cancellationToken);
    }
}
