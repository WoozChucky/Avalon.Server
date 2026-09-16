using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Avalon.Database;

/// <summary>
/// How a repository tracks an entity it is about to write.
///
/// <c>Add</c> walks the reachable navigation graph and marks every node that is <c>Detached</c> as
/// <c>Added</c>, key or no key. On a shared context the principal was usually still tracked
/// <c>Unchanged</c>, so the walk passed over it. A repository that creates a context per call
/// disposes it before returning, so everything it hands back is detached — and a navigation
/// pointing at a row that already exists would insert that row a second time.
///
/// These track the root for the write and treat anything reachable that already carries its key as
/// a row that exists. A reachable node with no key is genuinely new and is still inserted, so a
/// caller inserting a parent with new children keeps working.
/// </summary>
public static class DbContextWriteExtensions
{
    public static EntityEntry<TEntity> TrackForInsert<TEntity>(this DbContext context, TEntity entity)
        where TEntity : class
    {
        context.ChangeTracker.TrackGraph(entity, node =>
            node.Entry.State = ReferenceEquals(node.Entry.Entity, entity) || !node.Entry.IsKeySet
                ? EntityState.Added
                : EntityState.Unchanged);

        return context.Entry(entity);
    }

    public static EntityEntry<TEntity> TrackForUpdate<TEntity>(this DbContext context, TEntity entity)
        where TEntity : class
    {
        context.ChangeTracker.TrackGraph(entity, node =>
            node.Entry.State = ReferenceEquals(node.Entry.Entity, entity)
                ? EntityState.Modified
                : node.Entry.IsKeySet
                    ? EntityState.Unchanged
                    : EntityState.Added);

        return context.Entry(entity);
    }
}
