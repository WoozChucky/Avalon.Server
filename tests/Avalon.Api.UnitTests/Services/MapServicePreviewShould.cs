using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using MapType = Avalon.World.Public.Enums.MapType;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// A layout preview generates what the world server does, set pieces included: the forest's pool has no boss chunk of its
/// own, so a preview that left out the pool's groups would fail for every seed instead of showing the boss arena.
/// </summary>
public class MapServicePreviewShould
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
    public async Task Generate_the_preview_with_the_pools_set_pieces()
    {
        var mapId = new MapTemplateId(2);
        var poolId = new ChunkPoolId(3);
        ChunkTemplate entry = Chunk(1, N, "entry", PortalRole.Back);
        ChunkTemplate[] arenaCells = [Chunk(21, S), Chunk(22, 0), Chunk(23, 0), Chunk(24, 0, "boss")];
        var arena = new ChunkGroupDefinition("arena",
        [
            new ChunkGroupCell(arenaCells[0], 0, 0), new ChunkGroupCell(arenaCells[1], 1, 0),
            new ChunkGroupCell(arenaCells[2], 0, 1), new ChunkGroupCell(arenaCells[3], 1, 1),
        ]);

        var maps = Substitute.For<IMapTemplateRepository>();
        maps.FindByIdAsync(mapId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new MapTemplate { Id = mapId, MapType = MapType.Normal, Name = "forest", Description = string.Empty });
        var configs = Substitute.For<IProceduralMapConfigRepository>();
        configs.FindByTemplateIdAsync(mapId, Arg.Any<CancellationToken>()).Returns(new ProceduralMapConfig
        {
            MapTemplateId = mapId, ChunkPoolId = poolId, SpawnTableId = new SpawnTableId(1),
            MainPathMin = 2, MainPathMax = 2, HasBoss = true, BackPortalTargetMapId = 1,
        });
        var pool = new ChunkPool
        {
            Id = poolId, Name = "forest_pool",
            Memberships = [new ChunkPoolMembership { ChunkPoolId = poolId, ChunkTemplateId = entry.Id, Template = entry }],
        };
        var inputs = Substitute.For<IProceduralLayoutInputsResolver>();
        inputs.FindPoolAsync(poolId, Arg.Any<CancellationToken>()).Returns(pool);
        inputs.ResolveMembersAsync(pool, Arg.Any<CancellationToken>()).Returns(new ProceduralPoolResolution(
            [new ChunkPoolMember(entry, 1f)],
            new[] { entry }.Concat(arenaCells).ToDictionary(t => t.Id),
            [arena]));
        var options = Substitute.For<IOptionsSnapshot<MapAssetConfig>>();
        options.Value.Returns(new MapAssetConfig());
        var service = new MapService(maps, configs, inputs, Substitute.For<IChunkTemplateRepository>(),
            NullLoggerFactory.Instance, options);

        LayoutPreviewDto? preview = await service.PreviewLayoutAsync(2, seed: 5);

        Assert.NotNull(preview);
        Assert.Equal("c24", preview.BossChunk?.TemplateName);
        Assert.Equal(["c1", "c21", "c22", "c23", "c24"], preview.Chunks.Select(c => c.TemplateName).Order(StringComparer.Ordinal));
    }
}
