using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.World;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_ENTER_MAP)]
public class CEnterMapPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_ENTER_MAP;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The template ID of the map the client wants to enter.</summary>
    [ProtoMember(1)] public ushort TargetMapId { get; set; }

    public static NetworkPacket Create(ushort targetMapId, EncryptFunc encrypt)
    {
        using var memoryStream = new MemoryStream();

        var packet = new CEnterMapPacket { TargetMapId = targetMapId };
        Serializer.Serialize(memoryStream, packet);

        var buffer = encrypt(memoryStream.ToArray());

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer
        };
    }
}
