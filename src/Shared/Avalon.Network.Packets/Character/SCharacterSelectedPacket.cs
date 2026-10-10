using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
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

    public static OutboundPacket Create(CharacterInfo character, MapInfo map, PacketEncoder encoder)
        => encoder.Encode(
            new SCharacterSelectedPacket { Character = character, Map = map },
            PacketType, Flags, Protocol);
}

[ProtoContract]
public class MapInfo
{
    [ProtoMember(1)] public int MapId { get; set; }
    [ProtoMember(2)] public Guid InstanceId { get; set; }
    [ProtoMember(3)] public string Name { get; set; }
    [ProtoMember(4)] public string Description { get; set; }

    public override string ToString()
    {
        return $"MapId: {MapId}, InstanceId: {InstanceId}, Name: {Name}";
    }
}
