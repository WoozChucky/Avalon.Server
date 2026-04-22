using System.Linq.Expressions;
using Avalon.Database;
using Avalon.Domain.World;
using LinqKit;

namespace Avalon.Api.Contract;

public class SpellTemplatePaginateFilters : EntityPaginateFilter<SpellTemplate>
{
    public override Expression<Func<SpellTemplate, bool>> GetFilter()
    {
        return PredicateBuilder.New<SpellTemplate>(true);
    }

    public override Expression<Func<SpellTemplate, object>>? GetSortKeySelector()
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
