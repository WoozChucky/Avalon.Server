using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SExchangeWorldKeyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_EXCHANGE_WORLD_KEY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.ClearText;
    
    [ProtoMember(1)] public byte[]? PublicKey { get; set; }

    public static NetworkPacket Create(byte[]? publicKey)
    {
        using var memoryStream = new MemoryStream();
        
        var exchangeWorldKeyPacket = new SExchangeWorldKeyPacket()
        {
            PublicKey = publicKey
        };
        
        Serializer.Serialize(memoryStream, exchangeWorldKeyPacket);
        
        memoryStream.TryGetBuffer(out var buffer);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer.ToArray()
        };
    }
}
