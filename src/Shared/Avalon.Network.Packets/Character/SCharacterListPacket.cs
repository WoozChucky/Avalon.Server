using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

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
        CharacterInfo[] characters, Func<byte[], byte[]> encrypt)
    {
        using var memoryStream = new MemoryStream();

        var p = new SCharacterListPacket()
        {
            CharacterCount = characterCount,
            MaxCharacterCount = maxCharacterCount,
            Characters = characters
        };

        Serializer.Serialize(memoryStream, p);

        var encrypted = encrypt(memoryStream.ToArray());

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = encrypted
        };
    }
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
    [ProtoMember(9)] public bool Running { get; set; }
    [ProtoMember(10)] public ulong Experience { get; set; }
    [ProtoMember(11)] public ulong RequiredExperience { get; set; }

}
