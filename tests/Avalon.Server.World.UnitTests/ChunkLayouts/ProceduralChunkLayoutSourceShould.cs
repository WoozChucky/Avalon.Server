using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

/// <summary>
/// The seam that keeps the forest enterable: its pool has no boss chunk of its own (forest_boss_01 left it), so only the
/// boss arena set piece the library hands out can end a run. A source that stopped passing the pool's groups to the
/// generator would fail every attempt at the last step, for every player.
/// </summary>
public class ProceduralChunkLayoutSourceShould
{
    private const ushort N = 0b_0000_0000_0000_0010, S = 0b_0000_0000_1000_0000;

    private static ChunkTemplate Chunk(int id, ushort exits, string? tag = null, PortalRole? portal = null)
    {
        var t = new ChunkTemplate { Id = new ChunkTemplateId(id), Name = $"c{id}", GeometryFile = $"Chunks/c{id}.obj", CellSize = 30f, Exits = exits };
        if (tag is not null) t.SpawnSlots.Add(new ChunkSpawnSlot { Tag = tag, LocalX = 15, LocalY = 1, LocalZ = 15 });
        if (portal is { } role) t.PortalSlots.Add(new ChunkPortalSlot { Role = role, LocalX = 15, LocalY = 1, LocalZ = 5 });
        return t;
    }

    [Fact]
    public async Task Hand_the_pools_set_pieces_to_the_generator()
    {
        var poolId = new ChunkPoolId(3);
        var mapId = new MapTemplateId(2);
        var arena = new ChunkGroupDefinition("arena",
        [
            new ChunkGroupCell(Chunk(21, S), 0, 0), new ChunkGroupCell(Chunk(22, 0), 1, 0),
            new ChunkGroupCell(Chunk(23, 0), 0, 1), new ChunkGroupCell(Chunk(24, 0, "boss"), 1, 1),
        ]);
        var library = Substitute.For<IChunkLibrary>();
        library.GetByPool(poolId).Returns([new ChunkPoolMember(Chunk(1, N, "entry", PortalRole.Back), 1f)]);
        library.GetGroupsByPool(poolId).Returns([arena]);

        var configs = Substitute.For<IProceduralMapConfigRepository>();
        configs.FindByTemplateIdAsync(mapId, Arg.Any<CancellationToken>()).Returns(new ProceduralMapConfig
        {
            MapTemplateId = mapId,
            ChunkPoolId = poolId,
            SpawnTableId = new SpawnTableId(1),
            MainPathMin = 2,
            MainPathMax = 2,
            HasBoss = true,
            BackPortalTargetMapId = 1,
        });
        IServiceScopeFactory scopes = new ServiceCollection().AddScoped(_ => configs).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var source = new ProceduralChunkLayoutSource(NullLoggerFactory.Instance, library, scopes);

        ChunkLayout layout = await source.BuildAsync(
            new MapTemplate { Id = mapId, MapType = MapType.Normal, Name = "forest", Description = string.Empty },
            CancellationToken.None);

        Assert.Equal("arena", layout.BossChunk?.Group);
        Assert.Equal(4, layout.Chunks.Count(c => c.Group == "arena"));
    }
}
