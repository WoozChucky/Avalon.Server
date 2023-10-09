using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class SAuthResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_AUTH_RESULT;
    private const NetworkProtocol Protocol = NetworkProtocol.Tcp;
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public AuthResult Result { get; set; }
    
    public static NetworkPacket Create(int accountId, AuthResult result, Func<byte[], byte[]>? encryptFunc = null)
    {
        using var memoryStream = new MemoryStream();
        
        var packet = new SAuthResultPacket()
        {
            AccountId = accountId,
            Result = result,
        };
        
        Serializer.Serialize(memoryStream, packet);
        
        if (encryptFunc != null)
        {
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
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.ClearText,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
    
    public static NetworkPacket Create(AuthResult result, Func<byte[], byte[]>? encryptFunc = null)
    {
        using var memoryStream = new MemoryStream();
        
        var byePacket = new SAuthResultPacket()
        {
            Result = result,
        };
        
        Serializer.Serialize(memoryStream, byePacket);
        
        if (encryptFunc != null)
        {
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
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.ClearText,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}

public enum AuthResult : ushort
{
    INVALID_CREDENTIALS,
    WRONG_KEY,
    LOCKED,
    SUCCESS
}
