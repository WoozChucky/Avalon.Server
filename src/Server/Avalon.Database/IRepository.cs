using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Domain;

namespace Avalon.Database;

public interface IRepository<TEntity, in TKey> where TEntity : class, IDbEntity<TKey>
{
    Task<IList<TEntity>> FindAllAsync(bool track = false);
    Task<TEntity?> FindByIdAsync(TKey id, bool track = false);
    Task<IList<TEntity>> FindByAsync(Expression<Func<TEntity, bool>> predicate);
    
    Task<TEntity> CreateAsync(TEntity entity);
    Task<IList<TEntity>> CreateAsync(IList<TEntity> entities);
    Task<TEntity> UpdateAsync(TEntity entity);
    Task DeleteAsync(TKey id);
    void DisposeContext();
}
