using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>The one answer to a CItemUsePacket, sent to the requester only.</summary>
[ProtoContract]
public class SItemUseResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ITEM_USE_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The request's RequestId, echoed back.</summary>
    [ProtoMember(1)] public uint RequestId { get; set; }

    [ProtoMember(2)] public ItemUseResult Result { get; set; }

    /// <summary>On OnCooldown, the milliseconds left, rounded up so it is at least 1; 0 otherwise.</summary>
    [ProtoMember(3)] public uint CooldownMs { get; set; }

    /// <summary>On Refused, the line to show the player; absent otherwise.</summary>
    [ProtoMember(4)] public string? Message { get; set; }

    public static NetworkPacket Create(uint requestId, ItemUseResult result, uint cooldownMs, string? message,
        EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SItemUseResultPacket { RequestId = requestId, Result = result, CooldownMs = cooldownMs, Message = message },
            PacketType, Flags, Protocol, encrypt);
}
