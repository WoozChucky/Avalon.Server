using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

/// <summary>
/// Asks which instances of a portal's target map the character may enter (#1104), for the portal's menu. Answered by
/// <see cref="SInstanceListPacket" />.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_INSTANCE_LIST)]
public class CInstanceListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_INSTANCE_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The template ID of the map whose instances the client wants listed.</summary>
    [ProtoMember(1)] public ushort TargetMapId { get; set; }

    public static NetworkPacket Create(ushort targetMapId, EncryptFunc encrypt)
    {
        using var memoryStream = new MemoryStream();

        var packet = new CInstanceListPacket { TargetMapId = targetMapId };
        Serializer.Serialize(memoryStream, packet);

        byte[] buffer = encrypt(memoryStream.ToArray());

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
