using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterListPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_LIST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public int CharacterCount { get; set; }
    [ProtoMember(2)] public int MaxCharacterCount { get; set; }
    [ProtoMember(3)] public CharacterInfo[] Characters { get; set; }

    public static NetworkPacket Create(int characterCount, int maxCharacterCount,
        CharacterInfo[] characters, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SCharacterListPacket { CharacterCount = characterCount, MaxCharacterCount = maxCharacterCount, Characters = characters },
            PacketType, Flags, Protocol, encrypt);
}

[ProtoContract]
public class CharacterInfo
{
    [ProtoMember(1)] public uint CharacterId { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(3)] public int Level { get; set; }
    [ProtoMember(4)] public ushort Class { get; set; }
    [ProtoMember(5)] public float X { get; set; }
    [ProtoMember(6)] public float Y { get; set; }
    [ProtoMember(7)] public float Z { get; set; }
    [ProtoMember(8)] public float Orientation { get; set; }
    // 9 retired (was Running bool)
    [ProtoMember(10)] public ulong Experience { get; set; }
    [ProtoMember(11)] public ulong RequiredExperience { get; set; }
    [ProtoMember(12)] public float MovementSpeed { get; set; }

}
