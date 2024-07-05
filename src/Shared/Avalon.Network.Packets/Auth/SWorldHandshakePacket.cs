using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SWorldHandshakePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_HANDSHAKE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public ulong? AccountId { get; set; }
    [ProtoMember(2)] public bool Verified { get; set; }

    public static NetworkPacket Create(ulong accountId, bool verified, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var exchangeWorldKeyPacket = new SWorldHandshakePacket()
        {
            AccountId = accountId,
            Verified = verified
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
