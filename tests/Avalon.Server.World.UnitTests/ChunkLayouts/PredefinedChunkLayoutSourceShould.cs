using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Public.Enums;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

public class PredefinedChunkLayoutSourceShould
{
    private static MapTemplate Town(ushort id) => new()
    {
        Id = new MapTemplateId(id),
        MapType = MapType.Town,
        Name = $"town_{id}",
        Description = string.Empty,
    };

    [Fact]
    public async Task Throw_when_no_placements_exist()
    {
        var repo = Substitute.For<IMapChunkPlacementRepository>();
        repo.FindByMapAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(new List<MapChunkPlacement>());

        var library = Substitute.For<IChunkLibrary>();
        var source = PredefinedChunkLayoutSource.ForTesting(repo, library);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.BuildAsync(Town(1), CancellationToken.None));
    }

    [Fact]
    public async Task Build_layout_from_placements()
    {
        var mapId = new MapTemplateId(1);
        var chunkId = new ChunkTemplateId(7);
        var chunkTemplate = new ChunkTemplate
        {
            Id = chunkId,
            Name = "town_square_01",
            CellSize = 30f,
            PortalSlots = []
        };

        var placement = new MapChunkPlacement
        {
            MapTemplateId = mapId,
            ChunkTemplateId = chunkId,
            GridX = 0, GridZ = 0,
            Rotation = 0,
            IsEntry = true,
            EntryLocalX = 15f, EntryLocalY = 0f, EntryLocalZ = 15f
        };

        var repo = Substitute.For<IMapChunkPlacementRepository>();
        repo.FindByMapAsync(mapId, Arg.Any<CancellationToken>())
            .Returns(new List<MapChunkPlacement> { placement });

        var library = Substitute.For<IChunkLibrary>();
        library.LookupByIds(Arg.Any<IEnumerable<ChunkTemplateId>>())
            .Returns(new Dictionary<ChunkTemplateId, ChunkTemplate> { [chunkId] = chunkTemplate });

        var source = PredefinedChunkLayoutSource.ForTesting(repo, library);
        var layout = await source.BuildAsync(
            new MapTemplate
            {
                Id = mapId,
                MapType = MapType.Town,
                Name = "t",
                Description = string.Empty,
            },
            CancellationToken.None);

        Assert.Equal(0, layout.Seed);
        Assert.Single(layout.Chunks);
        Assert.Equal(chunkId, layout.EntryChunk.TemplateId);
        Assert.Equal(15f, layout.EntrySpawnWorldPos.x);
        Assert.Equal(15f, layout.EntrySpawnWorldPos.z);
        Assert.Null(layout.BossChunk);
    }

    [Fact]
    public async Task Skip_portal_slot_when_target_null()
    {
        var mapId = new MapTemplateId(1);
        var chunkId = new ChunkTemplateId(7);
        var chunkTemplate = new ChunkTemplate
        {
            Id = chunkId, Name = "town_x", CellSize = 30f,
            PortalSlots = new List<ChunkPortalSlot>
            {
                new() { Role = PortalRole.Forward, LocalX = 15, LocalY = 0, LocalZ = 15 }
            }
        };
        var placement = new MapChunkPlacement
        {
            MapTemplateId = mapId, ChunkTemplateId = chunkId,
            GridX = 0, GridZ = 0, Rotation = 0,
            IsEntry = true, EntryLocalX = 15, EntryLocalY = 0, EntryLocalZ = 15,
            ForwardPortalTargetMapId = null
        };
        var repo = Substitute.For<IMapChunkPlacementRepository>();
        repo.FindByMapAsync(mapId, Arg.Any<CancellationToken>())
            .Returns(new List<MapChunkPlacement> { placement });
        var library = Substitute.For<IChunkLibrary>();
        library.LookupByIds(Arg.Any<IEnumerable<ChunkTemplateId>>())
            .Returns(new Dictionary<ChunkTemplateId, ChunkTemplate> { [chunkId] = chunkTemplate });

        var source = PredefinedChunkLayoutSource.ForTesting(repo, library);
        var layout = await source.BuildAsync(
            new MapTemplate { Id = mapId, MapType = MapType.Town, Name = "t", Description = string.Empty },
            CancellationToken.None);

        Assert.Empty(layout.Portals);
    }

    [Fact]
    public async Task Emit_portal_with_resolved_target_when_set()
    {
        var mapId = new MapTemplateId(1);
        var chunkId = new ChunkTemplateId(7);
        var chunkTemplate = new ChunkTemplate
        {
            Id = chunkId, Name = "town_x", CellSize = 30f,
            PortalSlots = new List<ChunkPortalSlot>
            {
                new() { Role = PortalRole.Forward, LocalX = 15, LocalY = 0, LocalZ = 15 }
            }
        };
        var placement = new MapChunkPlacement
        {
            MapTemplateId = mapId, ChunkTemplateId = chunkId,
            GridX = 0, GridZ = 1, Rotation = 0,
            IsEntry = true, EntryLocalX = 15, EntryLocalY = 0, EntryLocalZ = 15,
            ForwardPortalTargetMapId = 2
        };
        var repo = Substitute.For<IMapChunkPlacementRepository>();
        repo.FindByMapAsync(mapId, Arg.Any<CancellationToken>())
            .Returns(new List<MapChunkPlacement> { placement });
        var library = Substitute.For<IChunkLibrary>();
        library.LookupByIds(Arg.Any<IEnumerable<ChunkTemplateId>>())
            .Returns(new Dictionary<ChunkTemplateId, ChunkTemplate> { [chunkId] = chunkTemplate });

        var source = PredefinedChunkLayoutSource.ForTesting(repo, library);
        var layout = await source.BuildAsync(
            new MapTemplate { Id = mapId, MapType = MapType.Town, Name = "t", Description = string.Empty },
            CancellationToken.None);

        Assert.Single(layout.Portals);
        var portal = layout.Portals[0];
        Assert.Equal(PortalRole.Forward, portal.Role);
        Assert.Equal((ushort)2, portal.TargetMapId);
        // World pos = origin (0, 0, 30) + local (15, 0, 15) = (15, 0, 45) for rotation 0
        Assert.Equal(15f, portal.WorldPos.x);
        Assert.Equal(45f, portal.WorldPos.z);
    }
}
