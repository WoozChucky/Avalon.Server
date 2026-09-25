using Avalon.Common.ValueObjects;

namespace Avalon.World.Loot;

/// <summary>
/// One thing a kill drops, before it is placed on the ground: a stack of one item template no
/// larger than its stack size, or a pile of copper.
/// </summary>
public readonly record struct RolledDrop(ItemTemplateId? ItemTemplateId, uint Count, ulong Gold)
{
    public static RolledDrop Item(ItemTemplateId id, uint count) => new(id, count, 0);

    public static RolledDrop GoldPile(ulong copper) => new(null, 0, copper);

    public bool IsGold => ItemTemplateId is null;
}
