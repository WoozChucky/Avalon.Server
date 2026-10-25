using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Inventory;
using Avalon.World.Reload;

namespace Avalon.World.Loot;

/// <summary>
/// A drop lying on the ground in one instance: a stack of one item template, a copy the drop rolled, or a pile of gold.
/// Immutable once placed, but for the name its rolled copy is described by, which it holds once worked out.
/// </summary>
/// <remarks>
/// Never persisted, and not an ItemInstance. A copy the drop rolled (#1179, <see cref="Rolled" />) was made whole at the
/// drop, its id, rarity, item level, name, base stats and modifiers included, and the picker gets exactly that copy;
/// any other item becomes an instance only when someone picks it up, through IInventoryService. Either way a drop
/// nobody takes costs no database rows. It lasts until it is picked up or its instance is disposed; there is no
/// despawn timer.
/// </remarks>
public sealed class GroundLoot
{
    public required ObjectGuid Guid { get; init; }

    /// <summary>World position, already on the ground.</summary>
    public required Vector3 Position { get; init; }

    /// <summary>Set for an item drop; null for a gold pile.</summary>
    public ItemTemplateId? ItemTemplateId { get; init; }

    /// <summary>How many of the item. 0 for a gold pile.</summary>
    public uint Count { get; init; }

    /// <summary>
    /// The copy the drop rolled (#1179), whose template is <see cref="ItemTemplateId" /> and whose count is 1; null for a
    /// drop that does not roll and for a gold pile. Nothing changes it while it lies here: a refused pickup leaves it as
    /// it was, and the pickup that takes it hands its modifier list to the picker's store.
    /// </summary>
    public RolledItem? Rolled { get; init; }

    /// <summary>Gold, for a gold pile. 0 for an item drop.</summary>
    public ulong Gold { get; init; }

    /// <summary>Who may take it before <see cref="FreeForAllAt"/>. Null when nobody owns it.</summary>
    public uint? OwnerCharacterId { get; init; }

    /// <summary>UTC. From then on anyone in the instance may take it.</summary>
    public DateTime FreeForAllAt { get; init; }

    public bool IsGold => ItemTemplateId is null;

    // The rolled copy's name, worked out at its first describe in an items generation (#1179): a drop is described at
    // its kill and again in every snapshot that shows it.
    private DescribedName _name;

    /// <summary>
    /// The name the rolled copy is described by in <paramref name="items" /> (ItemSlotDtoMapper.NameOf), held here once
    /// worked out; null for a drop that did not roll.
    /// </summary>
    internal string? NameIn(ItemsPatch items) => Rolled is { } rolled
        ? _name.Of(rolled.TemplateId, rolled.Rarity, rolled.RareName, rolled.Modifiers, items)
        : null;
}
