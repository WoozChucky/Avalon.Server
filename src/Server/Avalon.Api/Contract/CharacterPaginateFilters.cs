// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Linq.Expressions;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Domain.Characters;
using LinqKit;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Contract;

public class CharacterPaginateFilters : EntityPaginateFilter<Character>
{
    public string? NameLike { get; set; }
    public long? AccountId { get; set; }

    public override Expression<Func<Character, bool>> GetFilter()
    {
        var predicate = PredicateBuilder.New<Character>(true);

        if (AccountId is { } aid)
        {
            var accountId = new AccountId(aid);
            predicate = predicate.And(c => c.AccountId == accountId);
        }

        if (!string.IsNullOrEmpty(NameLike))
        {
            var pattern = $"%{NameLike}%";
            predicate = predicate.And(c => EF.Functions.ILike(c.Name, pattern));
        }

        return predicate;
    }

    public override Expression<Func<Character, object>>? GetSortKeySelector()
    {
        if (string.IsNullOrEmpty(SortBy))
            return c => c.Name;

        return SortBy.ToLower() switch
        {
            "name" => c => c.Name,
            "level" => c => c.Level,
            "experience" => c => c.Experience,
            "creationdate" => c => c.CreationDate,
            _ => null
        };
    }
}
