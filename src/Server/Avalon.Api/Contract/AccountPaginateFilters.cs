// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Linq.Expressions;
using Avalon.Database;
using Avalon.Domain.Auth;
using LinqKit;

namespace Avalon.Api.Contract;

public class AccountPaginateFilters : EntityPaginateFilter<Account>
{
    public string? Username { get; set; }
    public string? Email { get; set; }

    public override Expression<Func<Account, bool>> GetFilter()
    {
        var predicate = PredicateBuilder.New<Account>(true);

        if (Username != null)
            predicate = predicate.And(a => a.Username == Username);

        if (Email != null)
            predicate = predicate.And(a => a.Email == Email);

        return predicate;
    }

    public override Expression<Func<Account, object>>? GetSortKeySelector()
    {
        // Default to sorting by Username if no SortBy is provided
        if (string.IsNullOrEmpty(SortBy))
            return a => a.Username;

        // Map SortBy values to actual properties
        return SortBy.ToLower() switch
        {
            "username" => a => a.Username,
            "email" => a => a.Email,
            _ => null // No sorting if SortBy is unrecognized
        };
    }
}
