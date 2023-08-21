using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Avalon.Repositories
{
    public interface IRepository<TEntity, in TKey> where TEntity : class
    {
        Task<TEntity?> FindByIdAsync(TKey id);
        Task<IEnumerable<TEntity>> FindAllAsync();
        Task<IEnumerable<TEntity>> FindByAsync(Predicate<TEntity> predicate);
        Task<TEntity> SaveAsync(TEntity entity);
        Task<TEntity> UpdateAsync(TEntity entity);
        Task<bool> DeleteAsync(TEntity entity);
    }
}
