using Avalon.Common.ValueObjects;

namespace Avalon.World.Loot;

/// <summary>
/// One thing a kill drops, before it is placed on the ground: a stack of one item template no
/// larger than its stack size, or a pile of gold. A drop the item roller made into a copy (#1179) carries it as
/// <see cref="Rolled" />, and <see cref="ItemTemplateId" /> is then the copy's template (the base tier it rolled).
/// </summary>
public readonly record struct RolledDrop(ItemTemplateId? ItemTemplateId, uint Count, ulong Gold, RolledItem? Rolled = null)
{
    public static RolledDrop Item(ItemTemplateId id, uint count) => new(id, count, 0);

    public static RolledDrop GoldPile(ulong gold) => new(null, 0, gold);

    public bool IsGold => ItemTemplateId is null;
}
