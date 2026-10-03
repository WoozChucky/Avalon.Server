using ProtoBuf;

namespace Avalon.Network.Packets.Auras;

/// <summary>
/// One aura on one unit (auras). <see cref="InstanceKey" /> tells two copies of one aura apart (one per caster) and
/// stays the same while the unit holds that copy. <see cref="RemainingMs" /> is rounded up and counts from when the
/// packet was sent; <see cref="DurationMs" /> is the whole duration, for drawing a timer. <see cref="Action" /> is
/// absent in a list.
/// </summary>
[ProtoContract]
public class AuraEntryDto
{
    [ProtoMember(1)] public uint AuraId { get; set; }
    [ProtoMember(2)] public uint InstanceKey { get; set; }

    /// <summary>Who applied it; 0 for nobody or nobody known.</summary>
    [ProtoMember(3)] public ulong CasterGuid { get; set; }

    [ProtoMember(4)] public uint Stacks { get; set; }
    [ProtoMember(5)] public uint RemainingMs { get; set; }
    [ProtoMember(6)] public uint DurationMs { get; set; }
    [ProtoMember(7)] public AuraUpdateAction Action { get; set; }
}
