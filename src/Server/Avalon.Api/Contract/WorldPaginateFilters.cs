using System.Linq.Expressions;
using Avalon.Database;
using LinqKit;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Contract;

public class WorldPaginateFilters : EntityPaginateFilter<WorldEntity>
{
    public override Expression<Func<WorldEntity, bool>> GetFilter()
    {
        return PredicateBuilder.New<WorldEntity>(true);
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
