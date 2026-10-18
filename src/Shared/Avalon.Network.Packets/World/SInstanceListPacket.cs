using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

/// <summary>Whose instance an <see cref="InstanceChoiceDto" /> is (#1104). Values are only ever appended.</summary>
public enum InstanceKind : byte
{
    /// <summary>The character's own instance.</summary>
    Private = 0,

    /// <summary>An instance of the character's party.</summary>
    Party = 1,
}

/// <summary>One live instance the character may enter, as <see cref="SInstanceListPacket" /> lists it.</summary>
[ProtoContract]
public class InstanceChoiceDto
{
    /// <summary>What a <see cref="CEnterMapPacket" /> with <see cref="MapEntryChoice.Existing" /> names.</summary>
    [ProtoMember(1)] public Guid InstanceId { get; set; }

    /// <summary>The character's own private instance, or one of its party's.</summary>
    [ProtoMember(2)] public InstanceKind Kind { get; set; }

    /// <summary>Someone is inside, so no expiry timer is running.</summary>
    [ProtoMember(3)] public bool InUse { get; set; }

    /// <summary>For an empty instance, the time left before it expires; 0 when it is in use.</summary>
    [ProtoMember(4)] public uint ExpiresInMs { get; set; }

    /// <summary>The instance a <see cref="MapEntryChoice.Default" /> entry enters.</summary>
    [ProtoMember(5)] public bool IsDefault { get; set; }
}

/// <summary>
/// The answer to <see cref="CInstanceListPacket" /> (#1104): the character's own live private instances of the target
/// map and, in a party, the party's live instances of it. A client counts each expiry down locally from the answer.
/// A refusal carries the result and no instances.
/// </summary>
[ProtoContract]
public class SInstanceListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_INSTANCE_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The map the request asked about, echoed so a client can match the answer to its portal.</summary>
    [ProtoMember(1)] public ushort TargetMapId { get; set; }

    /// <summary><see cref="MapTransitionResult.Success" />, or the reason the portal would refuse the character.</summary>
    [ProtoMember(2)] public MapTransitionResult Result { get; set; }

    /// <summary>The character is in a party, so the party's instances are listed and plain entries take its default.</summary>
    [ProtoMember(3)] public bool InParty { get; set; }

    /// <summary>The character leads its party, so a client may offer a new party instance.</summary>
    [ProtoMember(4)] public bool IsLeader { get; set; }

    [ProtoMember(5)] public List<InstanceChoiceDto> Instances { get; set; } = [];

    public static OutboundPacket Create(ushort targetMapId, bool inParty, bool isLeader,
        List<InstanceChoiceDto> instances, PacketEncoder encoder)
    {
        SInstanceListPacket message = PacketEncoder.Scratch<SInstanceListPacket>();
        message.TargetMapId = targetMapId;
        message.Result = MapTransitionResult.Success;
        message.InParty = inParty;
        message.IsLeader = isLeader;
        message.Instances = instances;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }

    /// <summary>A refusal: the result and no instances.</summary>
    /// <remarks>
    /// No list: the scratch message comes back with no instances (<see cref="PacketEncoder.Scratch{T}" /> resets a
    /// collection to null), and protobuf-net writes a null repeated field as it writes an empty one, nothing, so a
    /// client reads an empty <see cref="Instances" />.
    /// </remarks>
    public static OutboundPacket CreateFailure(ushort targetMapId, MapTransitionResult result, PacketEncoder encoder)
    {
        SInstanceListPacket message = PacketEncoder.Scratch<SInstanceListPacket>();
        message.TargetMapId = targetMapId;
        message.Result = result;
        message.InParty = false;
        message.IsLeader = false;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
