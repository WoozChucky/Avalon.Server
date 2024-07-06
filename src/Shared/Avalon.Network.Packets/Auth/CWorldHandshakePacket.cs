using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_WORLD_HANDSHAKE)]
public class CWorldHandshakePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_WORLD_HANDSHAKE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public string Version { get; set; }

    public static NetworkPacket Create(string version, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var exchangeWorldKeyPacket = new CWorldHandshakePacket()
        {
            Version = version,
        };
        
        Serializer.Serialize(memoryStream, exchangeWorldKeyPacket);
        
        var buffer = encryptFunc(memoryStream.ToArray());
        
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
