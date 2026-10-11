using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Scenarios;
using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Navigation;

/// <summary>
/// The committed town's four-chunk layout, prepared once for a bake comparison. The timed tile bake uses the same
/// geometry, settings and TileNavMeshBuilder as production; the end-to-end bake also exercises production OBJ stitching.
/// </summary>
public sealed class NavmeshBakeFixture
{
    private readonly TownLibrary _library = new();
    private readonly ChunkLayout _layout = TownNavmesh.Layout();
    private readonly string _contentRoot = Path.GetDirectoryName(TownNavmesh.ContentRoot)!;

    public RcSampleInputGeomProvider Geometry { get; }

    public int ChunkCount => _layout.Chunks.Count;

    public NavmeshBakeFixture()
    {
        var vertices = new List<float>();
        var faces = new List<int>();
        foreach (PlacedChunk chunk in _layout.Chunks)
        {
            string name = _library.GetById(chunk.TemplateId).Name;
            var local = RcSampleInputGeomProvider.LoadFile(
                Path.Combine(_contentRoot, "Maps", "Chunks", $"{name}.obj"));
            int offset = vertices.Count / 3;
            for (int i = 0; i < local.vertices.Length; i += 3)
            {
                Vector3 world = ChunkRotation.LocalToWorld(local.vertices[i], local.vertices[i + 1],
                    local.vertices[i + 2], chunk.Rotation, _layout.CellSize, chunk.WorldPos);
                vertices.Add(world.x);
                vertices.Add(world.y);
                vertices.Add(world.z);
            }

            foreach (int face in local.faces)
                faces.Add(face + offset);
        }

        Geometry = new RcSampleInputGeomProvider(vertices, faces);
    }

    public NavMeshBuildResult Bake(bool keepIntermediateResults)
    {
        RcNavMeshBuildSettings settings = NavmeshBuildSettings.Create();
        settings.keepInterResults = keepIntermediateResults;
        NavMeshBuildResult result = new TileNavMeshBuilder().Build(Geometry, settings);
        if (result?.NavMesh is null)
            throw new InvalidOperationException("The committed town's tile bake returned no navmesh");
        return result;
    }

    public DtNavMesh BakeProduction() =>
        ChunkLayoutNavmeshBuilder.ForTesting(NullLoggerFactory.Instance, _library, _contentRoot)
            .BuildAsync(_layout, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Serialize tile payloads in spatial order. Worker completion order changes tile slots and polygon references,
    /// which are runtime identifiers rather than differences in navigation data.
    /// </summary>
    public static byte[][] CanonicalTileData(DtNavMesh mesh)
    {
        var data = new List<DtMeshData>();
        for (int i = 0; i < mesh.GetMaxTiles(); i++)
        {
            DtMeshTile tile = mesh.GetTile(i);
            if (tile?.data?.header is not null)
                data.Add(tile.data);
        }

        var tiles = new List<byte[]>();
        var writer = new DtMeshDataWriter();
        foreach (DtMeshData tile in data.OrderBy(d => d.header.x).ThenBy(d => d.header.y).ThenBy(d => d.header.layer))
        {
            using var stream = new MemoryStream();
            using var binary = new BinaryWriter(stream);
            writer.Write(binary, tile, RcByteOrder.LITTLE_ENDIAN, cCompatibility: false);
            tiles.Add(stream.ToArray());
        }

        return tiles.ToArray();
    }

    /// <summary>Give equivalent meshes identical tile references before comparing path tie-breaks.</summary>
    public static DtNavMesh CanonicalMesh(DtNavMesh mesh)
    {
        var canonical = new DtNavMesh();
        canonical.Init(mesh.GetParams(), NavmeshBuildSettings.VertsPerPoly);
        var reader = new DtMeshDataReader();
        foreach (byte[] data in CanonicalTileData(mesh))
        {
            using var stream = new MemoryStream(data);
            using var binary = new BinaryReader(stream);
            DtMeshData tile = reader.Read(binary, NavmeshBuildSettings.VertsPerPoly);
            canonical.AddTile(tile, 0, 0, out _);
        }

        return canonical;
    }

    private sealed class TownLibrary : IChunkLibrary
    {
        private static readonly string[] s_names = ["town_sw_01", "town_se_01", "town_nw_01", "town_ne_01"];

        public Task LoadAsync(CancellationToken ct) => Task.CompletedTask;

        public ChunkTemplate GetById(ChunkTemplateId id) =>
            new() { Id = id, Name = s_names[id.Value - 1], CellSize = 30f };

        public IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId) => [];

        public IReadOnlyList<ChunkGroupDefinition> GetGroupsByPool(ChunkPoolId poolId) => [];

        public IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids) =>
            ids.ToDictionary(id => id, GetById);
    }
}
