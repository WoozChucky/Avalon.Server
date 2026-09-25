using Avalon.Network.Packets.World;
using ProtoBuf;

namespace Avalon.Network.Packets.Loot;

/// <summary>
/// One drop on the ground: a stack of one item template, or a pile of copper. Exactly one of
/// <see cref="ItemTemplateId"/> and <see cref="Gold"/> is present.
/// </summary>
[ProtoContract]
public class LootDropDto
{
    /// <summary>Raw ObjectGuid, of type Loot. What CLootPickupPacket names.</summary>
    [ProtoMember(1)] public ulong LootGuid { get; set; }

    /// <summary>World position, already on the ground.</summary>
    [ProtoMember(2)] public Vector3Dto Position { get; set; } = new();

    /// <summary>Present for an item drop.</summary>
    [ProtoMember(3)] public ulong? ItemTemplateId { get; set; }

    /// <summary>How many of the item; 0 for a gold pile. Never more than the template's stack size.</summary>
    [ProtoMember(4)] public uint Count { get; set; }

    /// <summary>Present for a gold pile, in copper.</summary>
    [ProtoMember(5)] public ulong? Gold { get; set; }

    /// <summary>
    /// The character this drop is reserved for until <see cref="FreeForAllAt"/>. Absent when anyone
    /// may take it.
    /// </summary>
    [ProtoMember(6)] public uint? OwnerCharacterId { get; set; }

    /// <summary>
    /// When anyone may take it, as UTC ticks: the clock SPingPacket.ServerTimestamp carries. The
    /// client decides "yours" or "anyone's" from this and <see cref="OwnerCharacterId"/>; no packet
    /// is sent when the time passes.
    /// </summary>
    [ProtoMember(7)] public long FreeForAllAt { get; set; }
}
