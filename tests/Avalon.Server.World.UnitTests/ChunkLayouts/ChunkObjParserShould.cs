using Avalon.ChunkGen;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Maps.Navigation;
using DotRecast.Detour;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

/// <summary>
/// The town squares carry "usemtl" lines (one per shape, for the client's colours) and a client exporter may add
/// "mtllib", "s" or "g". The server's bake reads only "v" and "f" and must hand DotRecast a combined obj with
/// nothing else, so a tagged chunk bakes exactly as the same geometry untagged.
/// </summary>
public sealed class ChunkObjParserShould : IDisposable
{
    /// <summary>A floor and one 4 x 4 m box at (10-14, 10-14), written by the generator so every face is wound outward.</summary>
    private static readonly string Plain =
        ObjWriter.Write(new ChunkPiece("plain_01", [], [new BoxBlocker(10, 14, 10, 14)], [], ["test"]));

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"avalon-obj-parser-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Bake_a_chunk_with_material_lines_exactly_as_without_them()
    {
        string tagged = Plain
            .Replace("o Floor\n", "mtllib town.mtl\no Floor\nusemtl stone\ns off\n", StringComparison.Ordinal)
            .Replace("o Blocker_1\n", "g props\no Blocker_1\nusemtl wood\n", StringComparison.Ordinal);
        Assert.NotEqual(Plain, tagged);   // the replacements found their lines

        DtNavMesh plain = await Bake("plain_01", Plain);
        DtNavMesh withMaterials = await Bake("tagged_01", tagged);

        Assert.Equal(Counts(plain), Counts(withMaterials));
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(withMaterials);
        Assert.Equal(NavmeshGroundKind.Under, navigator.FindGround(new Vector3(5f, 1f, 5f), out Vector3 ground));
        Assert.InRange(ground.y, -0.3f, 0.3f);
        Vector3 stop = navigator.RaycastWalkable(new Vector3(5f, 0f, 12f), new Vector3(25f, 0f, 12f));
        Assert.True(stop.x < 10.1f, $"the walk went through the box to x = {stop.x}");
    }

    private async Task<DtNavMesh> Bake(string name, string obj)
    {
        string chunks = Path.Combine(_root, name, "Maps", "Chunks");
        Directory.CreateDirectory(chunks);
        await File.WriteAllTextAsync(Path.Combine(chunks, name + ".obj"), obj);

        var placed = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(0, [placed], placed, null, [], Vector3.zero, 30f);
        ChunkLayoutNavmeshBuilder builder = ChunkLayoutNavmeshBuilder.ForTesting(
            NullLoggerFactory.Instance, new OneChunkLibrary(name), Path.Combine(_root, name));
        return await builder.BuildAsync(layout, CancellationToken.None);
    }

    private static (int Tiles, int Polys, int Verts) Counts(DtNavMesh mesh)
    {
        int tiles = 0, polys = 0, verts = 0;
        for (int i = 0; i < mesh.GetMaxTiles(); i++)
        {
            DtMeshTile tile = mesh.GetTile(i);
            if (tile?.data?.header is null) continue;
            tiles++;
            polys += tile.data.header.polyCount;
            verts += tile.data.header.vertCount;
        }
        return (tiles, polys, verts);
    }

    private sealed class OneChunkLibrary(string name) : IChunkLibrary
    {
        public Task LoadAsync(CancellationToken ct) => Task.CompletedTask;
        public ChunkTemplate GetById(ChunkTemplateId id) => new() { Id = id, Name = name, CellSize = 30f };
        public IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId) => [];
        public IReadOnlyList<ChunkGroupDefinition> GetGroupsByPool(ChunkPoolId poolId) => [];
        public IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids) =>
            ids.ToDictionary(id => id, GetById);
    }
}
