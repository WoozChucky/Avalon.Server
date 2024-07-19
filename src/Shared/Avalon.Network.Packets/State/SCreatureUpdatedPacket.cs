using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.State;

[ProtoContract]
public class CreatureUpdate
{
    [ProtoMember(1)] public ulong Id { get; set; }
    [ProtoMember(2)] public byte[] Fields { get; set; }
}

[ProtoContract]
public class SCreatureUpdatedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CREATURE_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public List<CreatureUpdate> Updates { get; set; }

    public static NetworkPacket Create(List<(CreatureId creatureId, byte[] data)> updates, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new SCreatureUpdatedPacket()
        {
            Updates = updates.Select(x => new CreatureUpdate
            {
                Id = x.creatureId,
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
