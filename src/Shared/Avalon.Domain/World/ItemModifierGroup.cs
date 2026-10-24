using Avalon.Network.Packets.Combat;

namespace Avalon.Domain.World;

/// <summary>
/// One exclusive group of the modifier catalog (#1179): an affix family (each element of a family split by element is
/// its own group), a unique's modifier, or a base's implicits. An item never carries two modifiers of one group. Kind
/// and name live here, once, and every tier row of the group shares them.
/// </summary>
public class ItemModifierGroup
{
    public int Id { get; set; }

    public ItemModifierKind Kind { get; set; }

    /// <summary>The catalog code ("P8", "S13") of a prefix or suffix family; null for a unique or an implicit group.</summary>
    public string? Code { get; set; }

    /// <summary>The element of a family split by element (P8, P9, P10, S5); null otherwise.</summary>
    public DamageType? Element { get; set; }

    /// <summary>The affix name ("Ashen", "of the Thaw"), the unique's name, or the base's name for its implicit group.</summary>
    public string Name { get; set; } = null!;
}
