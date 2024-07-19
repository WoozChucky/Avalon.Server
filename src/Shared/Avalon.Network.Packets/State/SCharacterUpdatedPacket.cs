using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.State;

[ProtoContract]
public class CharacterUpdate
{
    [ProtoMember(1)] public ulong Id { get; set; }
    [ProtoMember(2)] public byte[] Fields { get; set; }
}

[ProtoContract]
public class SCharacterUpdatedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMGS_CHARACTER_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public List<CharacterUpdate> Updates { get; set; }

    public static NetworkPacket Create(List<(CharacterId characterId, byte[] data)> updates, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new SCharacterUpdatedPacket()
        {
            Updates = updates.Select(x => new CharacterUpdate
            {
                Id = x.characterId,
                Fields = x.data
            }).ToList()
        };

        Serializer.Serialize(memoryStream, p);

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
