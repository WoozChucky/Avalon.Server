using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Avalon.Domain;

namespace Avalon.Database;

public interface IRepository<TEntity, in TKey> where TEntity : class, IDbEntity<TKey>
{
    Task<IList<TEntity>> FindAllAsync();
    Task<TEntity?> FindByIdAsync(TKey id);
    Task<IList<TEntity>> FindByAsync(Expression<Func<TEntity, bool>> predicate);
    
    Task<TEntity> CreateAsync(TEntity entity);
    Task<TEntity> UpdateAsync(TEntity entity);
    Task DeleteAsync(TKey id);
    void DisposeContext();
}
