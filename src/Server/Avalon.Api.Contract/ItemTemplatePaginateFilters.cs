using System.Linq.Expressions;
using Avalon.Database;
using Avalon.Domain.World;
using LinqKit;

namespace Avalon.Api.Contract;

public class ItemTemplatePaginateFilters : EntityPaginateFilter<ItemTemplate>
{
    public override Expression<Func<ItemTemplate, bool>> GetFilter()
    {
        return PredicateBuilder.New<ItemTemplate>(true);
    }

    public override Expression<Func<ItemTemplate, object>>? GetSortKeySelector()
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
