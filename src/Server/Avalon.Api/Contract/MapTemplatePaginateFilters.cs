using System.Linq.Expressions;
using Avalon.Database;
using Avalon.Domain.World;
using LinqKit;

namespace Avalon.Api.Contract;

public class MapTemplatePaginateFilters : EntityPaginateFilter<MapTemplate>
{
    public override Expression<Func<MapTemplate, bool>> GetFilter()
    {
        return PredicateBuilder.New<MapTemplate>(true);
    }

    public override Expression<Func<MapTemplate, object>>? GetSortKeySelector()
    {
        if (string.IsNullOrEmpty(SortBy))
            return t => t.Id;

        return SortBy.ToLower() switch
        {
            "name" => t => t.Name,
            "id" => t => t.Id,
            _ => null
        };
    }
}
