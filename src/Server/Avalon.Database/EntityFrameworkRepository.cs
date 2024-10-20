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

    public async Task<IList<TEntity>> FindAllAsync(bool track = false)
    {
        return track
            ? await Context.Set<TEntity>().ToListAsync()
            : await FindAllNoTrackingAsync();
    }

    private async Task<IList<TEntity>> FindAllNoTrackingAsync()
    {
        return await Context.Set<TEntity>()
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<TEntity?> FindByIdAsync(TKey id, bool track = false)
    {
        return track
            ? await Context.Set<TEntity>()
                .FirstOrDefaultAsync(entity => EF.Property<TKey>(entity, nameof(IDbEntity<TKey>.Id))!.Equals(id))
            : await FindByIdNoTrackingAsync(id);
    }

    private async Task<TEntity?> FindByIdNoTrackingAsync(TKey id)
    {
        return await Context.Set<TEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => EF.Property<TKey>(entity, nameof(IDbEntity<TKey>.Id))!.Equals(id));
    }

    public async Task<IList<TEntity>> FindByAsync(Expression<Func<TEntity, bool>> predicate)
    {
        return await Context.Set<TEntity>()
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync();
    }

    public async Task<TEntity> CreateAsync(TEntity entity)
    {
        var entry = await Context.Set<TEntity>().AddAsync(entity);
        await Context.SaveChangesAsync();
        return entry.Entity;
    }

    public async Task<IList<TEntity>> CreateAsync(IList<TEntity> entities)
    {
        var entityList = new List<TEntity>();

        foreach (var entity in entities)
        {
            var entry = await Context.Set<TEntity>().AddAsync(entity);
            entityList.Add(entry.Entity);
        }

        await Context.SaveChangesAsync();

        return entityList;
    }

    public async Task<TEntity> UpdateAsync(TEntity entity)
    {
        // Detach existing entity if tracked
        var existingEntity = await Context.Set<TEntity>().FindAsync(entity.Id);
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

        await Context.SaveChangesAsync();
        return entry.Entity;
    }

    public async Task DeleteAsync(TKey id)
    {
        var entity = await FindByIdAsync(id);
        if (entity == null)
        {
            return;
        }

        Context.Set<TEntity>().Remove(entity);
        await Context.SaveChangesAsync();
    }
}
