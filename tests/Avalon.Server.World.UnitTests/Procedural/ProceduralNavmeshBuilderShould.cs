using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

public class ProceduralNavmeshBuilderShould
{
    [Fact(Skip = "Requires a chunks .obj file on disk; covered by smoke test.")]
    public async Task Bake_produces_navmesh_for_single_chunk()
    {
        var layout = new ChunkLayout(
            Seed: 1,
            Chunks: new[] { new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero) },
            EntryChunk: new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero),
            BossChunk: null, Portals: Array.Empty<PortalPlacement>(),
            EntrySpawnWorldPos: Vector3.zero, CellSize: 30f);

        var b = new ChunkLayoutNavmeshBuilder(NullLoggerFactory.Instance);
        var mesh = await b.BuildAsync(layout, CancellationToken.None);
        Assert.NotNull(mesh);
    }
}
