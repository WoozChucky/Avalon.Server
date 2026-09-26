using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Combat;

/// <summary>
/// The player's own PvP flag (#164): the reply to a toggle, the moment the off timer turns it off, and on
/// entering an instance. <see cref="OffInMs" /> is the time left on the off timer, rounded up, and 0 when
/// no timer is running. The flag stays hostile until the timer runs out.
/// </summary>
[ProtoContract]
public class SPvpStatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PVP_STATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public bool Enabled { get; set; }
    [ProtoMember(2)] public uint OffInMs { get; set; }

    public static NetworkPacket Create(bool enabled, uint offInMs, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SPvpStatePacket { Enabled = enabled, OffInMs = offInMs },
            PacketType, Flags, Protocol, encrypt);
}
