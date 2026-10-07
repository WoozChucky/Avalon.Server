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
        ExpressionStarter<Account> predicate = PredicateBuilder.New<Account>(true);

        if (Username != null)
            predicate = predicate.And(a => a.Username == Username);

        // Emails are stored normalised (#503), so the filter is compared in that form too.
        if (Email != null)
        {
            string email = AccountEmail.Normalise(Email);
            predicate = predicate.And(a => a.Email == email);
        }

        return predicate;
    }

    public override Expression<Func<Account, object>>? GetSortKeySelector()
    {
        // Default to sorting by Username if no SortBy is provided
        if (string.IsNullOrEmpty(SortBy))
            return a => a.Username;

        // Map SortBy values to actual properties
        return SortBy.ToLowerInvariant() switch
        {
            "username" => a => a.Username,
            // Account.Email is nullable; the query EF Core builds from this selector sorts a missing email as null.
#pragma warning disable CS8603
            "email" => a => a.Email,
#pragma warning restore CS8603
            _ => null // No sorting if SortBy is unrecognized
        };
    }
}
