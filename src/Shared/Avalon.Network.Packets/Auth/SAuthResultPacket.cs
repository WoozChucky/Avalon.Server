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
    [ProtoMember(3)] public byte[] PrivateKey { get; set; }
    
    public static NetworkPacket Create(int accountId, byte[] privateKey)
    {
        using var memoryStream = new MemoryStream();
        
        var byePacket = new SAuthResultPacket()
        {
            AccountId = accountId,
            Result = AuthResult.PENDING_KEY,
            PrivateKey = privateKey
        };
        
        Serializer.Serialize(memoryStream, byePacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.None,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
    
    public static NetworkPacket Create(int accountId, AuthResult result)
    {
        using var memoryStream = new MemoryStream();
        
        var byePacket = new SAuthResultPacket()
        {
            AccountId = accountId,
            Result = result,
        };
        
        Serializer.Serialize(memoryStream, byePacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.None,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
    
    public static NetworkPacket Create(AuthResult result)
    {
        using var memoryStream = new MemoryStream();
        
        var byePacket = new SAuthResultPacket()
        {
            Result = result,
        };
        
        Serializer.Serialize(memoryStream, byePacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.None,
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
    PENDING_KEY,
    WRONG_KEY,
    LOCKED,
    SUCCESS
}
