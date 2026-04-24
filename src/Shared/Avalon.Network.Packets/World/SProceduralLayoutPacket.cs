using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

[ProtoContract]
public class PlacedChunkDto
{
    [ProtoMember(1)] public int ChunkTemplateId { get; set; }
    [ProtoMember(2)] public short GridX { get; set; }
    [ProtoMember(3)] public short GridZ { get; set; }
    [ProtoMember(4)] public byte Rotation { get; set; }
}

[ProtoContract]
public class SProceduralLayoutPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PROCEDURAL_LAYOUT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public int Seed { get; set; }
    [ProtoMember(2)] public Guid InstanceId { get; set; }
    [ProtoMember(3)] public float CellSize { get; set; }
    [ProtoMember(4)] public List<PlacedChunkDto> Chunks { get; set; } = new();

    public static NetworkPacket Create(
        int seed,
        Guid instanceId,
        float cellSize,
        IReadOnlyList<(int templateId, short gridX, short gridZ, byte rotation)> chunks,
        EncryptFunc encrypt)
    {
        var pkt = new SProceduralLayoutPacket
        {
            Seed = seed,
            InstanceId = instanceId,
            CellSize = cellSize,
            Chunks = chunks.Select(c => new PlacedChunkDto
            {
                ChunkTemplateId = c.templateId,
                GridX = c.gridX,
                GridZ = c.gridZ,
                Rotation = c.rotation,
            }).ToList(),
        };
        return PacketSerializationHelper.Serialize(pkt, PacketType, Flags, Protocol, encrypt);
    }
}
