using System.Linq.Expressions;
using Avalon.Database;
using Avalon.Domain.World;
using LinqKit;

namespace Avalon.Api.Contract;

public class CreatureTemplatePaginateFilters : EntityPaginateFilter<CreatureTemplate>
{
    public override Expression<Func<CreatureTemplate, bool>> GetFilter()
    {
        return PredicateBuilder.New<CreatureTemplate>(true);
    }

    public override Expression<Func<CreatureTemplate, object>>? GetSortKeySelector()
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
