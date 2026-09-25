using System.Linq.Expressions;
using Avalon.Common.Accounts;
using Avalon.Database;
using LinqKit;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Contract;

public class WorldPaginateFilters : EntityPaginateFilter<WorldEntity>
{
    /// <summary>
    /// The access level of the caller the page is for. Only worlds that level may enter are listed
    /// and counted, by the rule the TCP world list applies (#452). The default, no level, lists
    /// nothing, so a caller that forgets to set it fails closed.
    /// </summary>
    // Fully qualified: inside Avalon.Api.Contract the wire enum of the same name would win.
    public Avalon.Common.Accounts.AccountAccessLevel CallerAccessLevel { get; init; }

    public override Expression<Func<WorldEntity, bool>> GetFilter()
    {
        // AccessLevels.ForWorld(...).Allows(...) in a form the database can evaluate, so the page
        // and its total count cover only visible worlds. A mask test, never "<=" (#447).
        var enterable = AccessLevels.WorldsEnterableBy(CallerAccessLevel);
        return PredicateBuilder.New<WorldEntity>(w => (w.AccessLevelRequired & enterable) != 0);
    }

    public override Expression<Func<WorldEntity, object>>? GetSortKeySelector()
    {
        if (string.IsNullOrEmpty(SortBy))
            return w => w.Name;

        return SortBy.ToLower() switch
        {
            "name" => w => w.Name,
            "status" => w => w.Status,
            _ => null
        };
    }
}
