using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Domain;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database;

public class EntityFrameworkRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class, IDbEntity<TKey>
{
    protected readonly DbContext Context;

    protected EntityFrameworkRepository(DbContext dbContext)
    {
        Context = dbContext;
    }

    public async Task<IList<TEntity>> FindAllAsync()
    {
        return await Context.Set<TEntity>()
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<TEntity?> FindByIdAsync(TKey id)
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
        Context.Set<TEntity>().Add(entity);
        await Context.SaveChangesAsync();

        return entity;
    }

    public async Task<TEntity> UpdateAsync(TEntity entity)
    {
        Context.Set<TEntity>().Update(entity);
        await Context.SaveChangesAsync();

        return entity;
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
