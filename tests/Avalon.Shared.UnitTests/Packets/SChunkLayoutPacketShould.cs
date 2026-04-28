using System;
using System.Collections.Generic;
using System.IO;
using Avalon.Network.Packets.World;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

public class SChunkLayoutPacketShould
{
    [Fact]
    public void Round_trip_via_protobuf_preserves_all_fields()
    {
        var pkt = new SChunkLayoutPacket
        {
            Seed = 42,
            InstanceId = Guid.NewGuid(),
            CellSize = 30f,
            Chunks = new List<PlacedChunkDto>
            {
                new() { ChunkTemplateId = 7, GridX = 1, GridZ = 2, Rotation = 3, ChunkName = "forest_entry_01" }
            },
            EntrySpawn = new Vector3Dto { X = 15, Y = 0, Z = 17 }
        };

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, pkt);
        ms.Position = 0;
        var round = Serializer.Deserialize<SChunkLayoutPacket>(ms);

        Assert.Equal(pkt.Seed, round.Seed);
        Assert.Equal(pkt.InstanceId, round.InstanceId);
        Assert.Equal(pkt.CellSize, round.CellSize);
        Assert.Single(round.Chunks);
        Assert.Equal(7, round.Chunks[0].ChunkTemplateId);
        Assert.Equal((short)1, round.Chunks[0].GridX);
        Assert.Equal((short)2, round.Chunks[0].GridZ);
        Assert.Equal((byte)3, round.Chunks[0].Rotation);
        Assert.Equal("forest_entry_01", round.Chunks[0].ChunkName);
        Assert.Equal(15f, round.EntrySpawn.X);
        Assert.Equal(0f, round.EntrySpawn.Y);
        Assert.Equal(17f, round.EntrySpawn.Z);
    }

    [Fact]
    public void Round_trip_preserves_portals()
    {
        var pkt = new SChunkLayoutPacket
        {
            Seed = 0,
            InstanceId = Guid.NewGuid(),
            CellSize = 30f,
            EntrySpawn = new Vector3Dto { X = 15, Y = 0, Z = 15 },
            Portals = new List<PortalPlacementDto>
            {
                new() { Role = 1 /* Forward */, WorldPos = new Vector3Dto { X = 15, Y = 0, Z = 45 }, Radius = 3f, TargetMapId = 2 }
            }
        };

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, pkt);
        ms.Position = 0;
        var round = Serializer.Deserialize<SChunkLayoutPacket>(ms);

        Assert.Single(round.Portals);
        Assert.Equal(1, round.Portals[0].Role);
        Assert.Equal((ushort)2, round.Portals[0].TargetMapId);
        Assert.Equal(45f, round.Portals[0].WorldPos.Z);
        Assert.Equal(3f, round.Portals[0].Radius);
    }
}
