using ProtoBuf;

namespace Avalon.Network.Packets.Items;

/// <summary>
/// One base stat a copy rolled (#1179): its armour, evasion, ward, block chance or weapon numbers. Not a modifier: the
/// client shows it as the copy's own number, which the copy's local modifiers then scale.
/// </summary>
[ProtoContract]
public class ItemBaseStatDto
{
    [ProtoMember(1)] public BaseStat Stat { get; set; }

    /// <summary>In the stat's unit (<see cref="BaseStat" />).</summary>
    [ProtoMember(2)] public int Value { get; set; }
}
