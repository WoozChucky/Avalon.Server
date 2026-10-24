using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using ItemRarity = Avalon.Common.ItemRarity;

namespace Avalon.Api.Worlds.Services;

/// <summary>
/// An item as the public tooltip and its link preview show it (#1179): the template with the lines every copy rolls
/// within, and for a unique the base it names, whose base-stat ranges and implicit every copy of it rolls before the
/// unique's own lines, as the world rolls them.
/// </summary>
public static class PublicItems
{
    public static async Task<PublicItemDto> ToPublicAsync(
        ItemTemplate template, IItemTemplateRepository items, IItemModifierRepository modifiers, CancellationToken ct)
    {
        ItemModifierCatalog catalog = await modifiers.GetLinkedToAsync(template, ct);
        if (template.Rarity != ItemRarity.Unique || template.BaseTemplateId is not { } baseId
            || await items.FindByIdAsync(baseId, track: false, ct) is not { } named)
        {
            return template.ToPublicDto(catalog.LinkedTo(template.Id));
        }

        ItemModifierCatalog inherited = await modifiers.GetLinkedToAsync(named, ct);
        return template.ToPublicDto(catalog.LinkedTo(template.Id), named, inherited.LinkedTo(named.Id));
    }
}
