using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CExchangeWorldKeyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_EXCHANGE_WORLD_KEY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.ClearText;
    
    [ProtoMember(1)] public byte[] WorldKey { get; set; }
    [ProtoMember(2)] public byte[] PublicKey { get; set; }

    public static NetworkPacket Create(byte[] worldKey, byte[] publicKey)
    {
        using var memoryStream = new MemoryStream();
        
        var exchangeWorldKeyPacket = new CExchangeWorldKeyPacket()
        {
            WorldKey = worldKey,
            PublicKey = publicKey
        };
        
        Serializer.Serialize(memoryStream, exchangeWorldKeyPacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
