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
        Directory = string.Empty
    };

    [Fact]
    public async Task Throw_when_no_placements_exist()
    {
        var repo = Substitute.For<IMapChunkPlacementRepository>();
        repo.FindByMapAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(new List<MapChunkPlacement>());

        var library = Substitute.For<IChunkLibrary>();
        var source = new PredefinedChunkLayoutSource(repo, library);

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

        var source = new PredefinedChunkLayoutSource(repo, library);
        var layout = await source.BuildAsync(
            new MapTemplate
            {
                Id = mapId,
                MapType = MapType.Town,
                Name = "t",
                Description = string.Empty,
                Directory = string.Empty
            },
            CancellationToken.None);

        Assert.Equal(0, layout.Seed);
        Assert.Single(layout.Chunks);
        Assert.Equal(chunkId, layout.EntryChunk.TemplateId);
        Assert.Equal(15f, layout.EntrySpawnWorldPos.x);
        Assert.Equal(15f, layout.EntrySpawnWorldPos.z);
        Assert.Null(layout.BossChunk);
    }
}
