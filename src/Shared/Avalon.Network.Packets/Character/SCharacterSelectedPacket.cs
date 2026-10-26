using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Items;
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

    /// <summary>The world's item prices, so the client shows a Workbench price before it asks (#1221).</summary>
    [ProtoMember(3)] public ItemPricesDto ItemPrices { get; set; }

    public static OutboundPacket Create(CharacterInfo character, MapInfo map, ItemPricesDto itemPrices, PacketEncoder encoder)
    {
        SCharacterSelectedPacket message = PacketEncoder.Scratch<SCharacterSelectedPacket>();
        message.Character = character;
        message.Map = map;
        message.ItemPrices = itemPrices;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
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
