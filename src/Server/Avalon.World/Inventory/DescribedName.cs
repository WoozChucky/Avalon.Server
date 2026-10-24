using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Reload;

namespace Avalon.World.Inventory;

/// <summary>
/// The name a copy is described by (#1179), held as a field beside its modifiers wherever the world keeps them: a held
/// copy's entry in <see cref="ItemModifierStore" />, and a drop on the ground. It is worked out once per items generation
/// (<see cref="ItemSlotDtoMapper.NameOf" />), so describing the same copy again builds no string; a /reload items is a new
/// generation, so a renamed base or affix still reaches every copy at its next describe, and a copy whose modifiers
/// change gets a new entry, which starts unnamed. A mutable struct: kept only in a field and used in place.
/// </summary>
internal struct DescribedName
{
    private ItemsPatch? _namedIn;
    private string? _name;

    /// <summary>The copy's name in <paramref name="items" />: the one held when it was worked out in that generation.</summary>
    public string? Of(ItemTemplateId templateId, ItemRarity rarity, string? rareName,
        IReadOnlyList<ItemInstanceModifier> modifiers, ItemsPatch items)
    {
        // By reference: ItemsPatch is a record, whose == compares its members.
        if (!ReferenceEquals(_namedIn, items))
        {
            _name = ItemSlotDtoMapper.NameOf(templateId, rarity, rareName, modifiers, items);
            _namedIn = items;
        }

        return _name;
    }
}
