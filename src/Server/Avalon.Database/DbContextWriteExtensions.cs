using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Avalon.Database;

/// <summary>
/// How a repository tracks the one entity it is about to write.
///
/// A repository write covers a single row. It never writes a graph, and it does not try to work
/// out what a caller meant by a navigation: anything reachable through one is refused.
///
/// That is a rule about signals rather than about EF. <c>Add</c> walks the reachable graph and
/// marks every detached node <c>Added</c>, and a context created for one call disposes before the
/// entity comes back, so everything a caller hands over is detached — a navigation pointing at a
/// row that exists inserted it again. Discriminating on the key does not fix it:
/// <c>EntityEntry.IsKeySet</c> is <c>true</c> unconditionally for a key with no store generation,
/// so for the client-keyed types a "no key means new" test can never say new, and the row is
/// dropped with no exception and a success returned. Refusing the graph is the only reading that
/// cannot be silently wrong.
///
/// Owned entities are exempt. They are part of the root's row, have no key of their own to be
/// named by, and there is nothing else a caller could do with them.
/// </summary>
public static class DbContextWriteExtensions
{
    public static EntityEntry<TEntity> TrackForInsert<TEntity>(this DbContext context, TEntity entity)
        where TEntity : class =>
        Track(context, entity, EntityState.Added);

    public static EntityEntry<TEntity> TrackForUpdate<TEntity>(this DbContext context, TEntity entity)
        where TEntity : class =>
        Track(context, entity, EntityState.Modified);

    private static EntityEntry<TEntity> Track<TEntity>(DbContext context, TEntity entity, EntityState state)
        where TEntity : class
    {
        context.ChangeTracker.TrackGraph(entity, node =>
        {
            if (!ReferenceEquals(node.Entry.Entity, entity) && !node.Entry.Metadata.IsOwned())
            {
                throw new InvalidOperationException(
                    $"A repository write covers one entity, and {typeof(TEntity).Name} reaches " +
                    $"{node.Entry.Entity.GetType().Name} through a navigation. Name that row by its " +
                    "foreign key and leave the navigation null, or write the graph yourself on one " +
                    "context through IDbTransactionRunner.");
            }

            node.Entry.State = state;
        });

        return context.Entry(entity);
    }
}
