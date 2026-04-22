// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace Avalon.Database;

public abstract class EntityPaginateFilter<TEntity> where TEntity : class
{
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize { get; set; } = 10;

    public string? SortBy  { get; set; }

    /// <summary>
    /// Return an EF-translatable predicate.
    /// </summary>
    public abstract Expression<Func<TEntity, bool>> GetFilter();

    /// <summary>
    /// Return the key selector used for ORDER BY, or null to skip ordering.
    /// </summary>
    public abstract Expression<Func<TEntity, object>>? GetSortKeySelector();

    public virtual SortDirection GetSortDirection()
    {
        return SortDirection.Ascending;
    }
}

public enum SortDirection
{
    Ascending,
    Descending
}
