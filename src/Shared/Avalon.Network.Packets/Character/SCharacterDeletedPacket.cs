using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterDeletedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_DELETED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int AccountId { get; set; }
    [ProtoMember(2)] public SCharacterDeletedResult Result { get; set; }

    public static NetworkPacket Create(int accountId, SCharacterDeletedResult result)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new SCharacterDeletedPacket()
        {
            AccountId = accountId,
            Result = result
        };
        
        Serializer.Serialize(memoryStream, authPacket);
        
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

public enum SCharacterDeletedResult : short
{
    Success = 0,
    InGame = 1,
    InternalError = 2,
    Mail = 3,
}
