using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Reload;

namespace Avalon.World.Inventory;

/// <summary>
/// The name a held copy is described by (#1179), held as a field beside its modifiers on its entry in
/// <see cref="ItemModifierStore" />, and only once the copy is identified. It is worked out once per items generation
/// (<see cref="ItemSlotDtoMapper.NameOf" />), so describing the same copy again builds no string; a /reload items is a new
/// generation, so a renamed base or affix still reaches every copy at its next describe, and a copy whose modifiers
/// change gets a new entry, which starts unnamed. It keeps the generation's stamp (<see cref="ItemsPatch.Generation" />),
/// never the patch itself, so a held name keeps no old generation's templates and catalog alive. A mutable struct: kept
/// only in a field and used in place.
/// </summary>
internal struct DescribedName
{
    // The stamp of the generation the name was worked out in; 0 (no generation has it) until the first describe.
    private long _namedIn;
    private string? _name;

    /// <summary>The copy's name in <paramref name="items" />: the one held when it was worked out in that generation.</summary>
    public string? Of(ItemTemplateId templateId, ItemRarity rarity, string? rareName,
        IReadOnlyList<ItemInstanceModifier> modifiers, ItemsPatch items)
    {
        if (_namedIn != items.Generation)
        {
            _name = ItemSlotDtoMapper.NameOf(templateId, rarity, rareName, modifiers, items);
            _namedIn = items.Generation;
        }

        return _name;
    }
}
