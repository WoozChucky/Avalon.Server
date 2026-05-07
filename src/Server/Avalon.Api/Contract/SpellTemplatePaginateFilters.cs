using System.Linq.Expressions;
using Avalon.Database;
using Avalon.Domain.World;
using LinqKit;

namespace Avalon.Api.Contract;

public class SpellTemplatePaginateFilters : EntityPaginateFilter<AbilityTemplate>
{
    public override Expression<Func<AbilityTemplate, bool>> GetFilter()
    {
        return PredicateBuilder.New<AbilityTemplate>(true);
    }

    public override Expression<Func<AbilityTemplate, object>>? GetSortKeySelector()
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
