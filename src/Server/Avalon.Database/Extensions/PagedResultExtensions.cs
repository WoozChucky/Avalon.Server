// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Database.Extensions;

public static class PagedResultExtensions
{
    public static PagedResult<TDto> MapTo<TEntity, TDto>(
        this PagedResult<TEntity> source,
        Func<TEntity, TDto> mapper)
    {
        return new PagedResult<TDto>(
            source.Page,
            source.PageSize,
            source.TotalCount,
            source.Items.Select(mapper).ToList());
    }
}
