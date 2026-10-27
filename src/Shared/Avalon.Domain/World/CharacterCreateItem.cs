using Avalon.Common;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One item a new character of a class carries: a copy of <see cref="TemplateId" /> in Bag slot <see cref="Slot" />. A
/// balance table, seeded with its class's <see cref="CharacterCreateInfo" />.
/// </summary>
public class CharacterCreateItem
{
    public CharacterClass Class { get; set; }

    /// <summary>The Bag slot the item is created in.</summary>
    public byte Slot { get; set; }

    public ItemTemplateId TemplateId { get; set; }
}
