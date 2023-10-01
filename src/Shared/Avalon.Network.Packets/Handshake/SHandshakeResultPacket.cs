using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Handshake;

[ProtoContract]
public class SHandshakeResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_SERVER_HANDSHAKE_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;

    [ProtoMember(1)] public bool Verified { get; set; }

    public static NetworkPacket Create(bool verified, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var packet = new SHandshakeResultPacket()
        {
            Verified = verified
        };
        
        Serializer.Serialize(memoryStream, packet);
        

        var buffer = encryptFunc(memoryStream.ToArray());
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.Encrypted,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer.ToArray()
        };
    }
}
