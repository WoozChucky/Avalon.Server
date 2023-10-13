using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterSelectedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_SELECTED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public CharacterInfo Character { get; set; }
    [ProtoMember(2)] public MapInfo Map { get; set; }

    public static NetworkPacket Create(CharacterInfo character, MapInfo map, Func<byte[], byte[]> encrypt)
    {
        using var memoryStream = new MemoryStream();
        
        var authPacket = new SCharacterSelectedPacket()
        {
            Character = character,
            Map = map
        };
        
        Serializer.Serialize(memoryStream, authPacket);
        
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

[ProtoContract]
public class MapInfo
{
    [ProtoMember(1)] public int MapId { get; set; }
    [ProtoMember(2)] public Guid InstanceId { get; set; }
    [ProtoMember(3)] public string Name { get; set; }
    [ProtoMember(4)] public string Description { get; set; }
    [ProtoMember(5)] public string Atlas { get; set; }
    [ProtoMember(6)] public string Directory { get; set; }
    [ProtoMember(7)] public byte[] Data { get; set; }
    [ProtoMember(8)] public byte[][] TilesetsData { get; set; }

    public override string ToString()
    {
        return $"MapId: {MapId}, InstanceId: {InstanceId}, Name: {Name}, Atlas: {Atlas}, Directory: {Directory}";
    }
}
