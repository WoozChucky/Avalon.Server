using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.World;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.Combat;

/// <summary>How a projectile's flight met something, or ended (#479). Append-only, as every wire enum.</summary>
public enum ProjectileImpactReason
{
    /// <summary>Never sent: what a reader holds for a reason it does not know.</summary>
    Unknown = 0,

    /// <summary>It struck <see cref="SProjectileImpactPacket.Target" /> and its flight ended there.</summary>
    Hit = 1,

    /// <summary>It struck <see cref="SProjectileImpactPacket.Target" /> and flew on through.</summary>
    Pierced = 2,

    /// <summary>It flew its whole reach and ended there, hitting nothing more.</summary>
    Expired = 3,

    /// <summary>A wall stopped it before its reach.</summary>
    Blocked = 4,
}

/// <summary>
/// A projectile struck a unit or ended its flight (#479), sent on the tick it happens to everyone who hears that point,
/// so a client draws the impact where and when it was. A hit is sent before the damage it deals, so the damage that
/// follows on the same tick belongs to it. A piercing projectile sends one <see cref="ProjectileImpactReason.Pierced" />
/// per unit it passes through, then one <see cref="ProjectileImpactReason.Expired" /> or
/// <see cref="ProjectileImpactReason.Blocked" /> where it ends. A projectile dropped with its caster (a caster that
/// left, died or went home) sends none: its remove is all there is.
/// </summary>
[ProtoContract]
public class SProjectileImpactPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PROJECTILE_IMPACT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The projectile's guid, as its world-state add carried it.</summary>
    [ProtoMember(1)] public ulong ProjectileGuid { get; set; }

    [ProtoMember(2)] public ulong Caster { get; set; }
    [ProtoMember(3)] public uint AbilityId { get; set; }

    /// <summary>The cast that fired it, as its start, fired and finish broadcasts carried it.</summary>
    [ProtoMember(4)] public uint CastId { get; set; }

    /// <summary>Where it was at the impact, at the height it flies: on a hit, the point of its flight nearest the unit.</summary>
    // The wire contract stays as it is: IsRequired would change what is serialized (WireSchemaShould pins it).
#pragma warning disable PBN0022
    [ProtoMember(5)] public Vector3Dto Position { get; set; } = new();
#pragma warning restore PBN0022

    [ProtoMember(6)] public ProjectileImpactReason Reason { get; set; }

    /// <summary>The unit struck, on a hit or a pierce; 0 when it ended without striking one.</summary>
    [ProtoMember(7)] public ulong Target { get; set; }

    /// <summary>The world tick it happened on, the clock a start broadcast's <c>WorldTick</c> runs on.</summary>
    [ProtoMember(8)] public long WorldTick { get; set; }

    public static OutboundPacket Create(ulong projectile, ulong caster, uint abilityId, uint castId,
        Vector3Dto position, ProjectileImpactReason reason, ulong target, long worldTick, PacketEncoder encoder)
    {
        SProjectileImpactPacket message = PacketEncoder.Scratch<SProjectileImpactPacket>();
        message.ProjectileGuid = projectile;
        message.Caster = caster;
        message.AbilityId = abilityId;
        message.CastId = castId;
        message.Position = position;
        message.Reason = reason;
        message.Target = target;
        message.WorldTick = worldTick;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
