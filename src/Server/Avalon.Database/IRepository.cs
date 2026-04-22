using System.Linq.Expressions;
using Avalon.Domain;

namespace Avalon.Database;

public interface IRepository<TEntity, in TKey> where TEntity : class, IDbEntity<TKey>
{
    Task<PagedResult<TEntity>> PaginateAsync(EntityPaginateFilter<TEntity> filter, bool track = false, CancellationToken cancellationToken = default);
    Task<List<TEntity>> FindAllAsync(bool track = false, CancellationToken cancellationToken = default);
    Task<TEntity?> FindByIdAsync(TKey id, bool track = false, CancellationToken cancellationToken = default);
    Task<List<TEntity>> FindByAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<List<TEntity>> CreateAsync(List<TEntity> entities, CancellationToken cancellationToken = default);
    Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);
}
