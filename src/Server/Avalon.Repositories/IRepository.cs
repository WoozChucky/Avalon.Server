using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DapperExtensions.Predicate;

namespace Avalon.Repositories
{
    public interface IRepository<TEntity, in TKey> where TEntity : class
    {
        Task<TEntity?> FindByIdAsync(TKey id);
        Task<IEnumerable<TEntity>> FindAllAsync();
        Task<IEnumerable<TEntity>> FindByAsync(IFieldPredicate predicate);
        Task<IEnumerable<TEntity>> FindByAsync(Expression<Func<TEntity, bool>> predicate);
        Task<TEntity> SaveAsync(TEntity entity);
        Task<TEntity> UpdateAsync(TEntity entity);
        Task<bool> DeleteAsync(TEntity entity);
    }
}
