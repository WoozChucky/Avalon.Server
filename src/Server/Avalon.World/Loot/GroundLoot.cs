using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;

namespace Avalon.World.Loot;

/// <summary>
/// A drop lying on the ground in one instance: a stack of one item template, or a pile of copper.
/// Immutable once placed.
/// </summary>
/// <remarks>
/// Never persisted, and not an ItemInstance: the item instance and its id are created only when
/// someone picks the drop up, through IInventoryService, so a drop nobody takes costs no database
/// rows. It lasts until it is picked up or its instance is disposed; there is no despawn timer.
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

    /// <summary>Copper, for a gold pile. 0 for an item drop.</summary>
    public ulong Gold { get; init; }

    /// <summary>Who may take it before <see cref="FreeForAllAt"/>. Null when nobody owns it.</summary>
    public uint? OwnerCharacterId { get; init; }

    /// <summary>UTC. From then on anyone in the instance may take it.</summary>
    public DateTime FreeForAllAt { get; init; }

    public bool IsGold => ItemTemplateId is null;
}
