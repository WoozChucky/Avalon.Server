using Avalon.ChunkGen;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// The kit's bounds table is measured from each model's glTF: its vertices' box carried through the node transforms,
/// which is what the art draws once the client places the model.
/// </summary>
public sealed class KitBoundsShould : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"avalon-kit-bounds-{Guid.NewGuid():N}");

    public KitBoundsShould() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Measure_a_model_through_its_node_transform()
    {
        // A quarter turn about +Y (x' = z, z' = -x), then a move of (1, 0, 2). The box is not symmetric in Z, so a turn
        // the other way (x' = -z) lands elsewhere.
        const string Gltf = """
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [{ "nodes": [0] }],
              "nodes": [{ "mesh": 0, "translation": [1, 0, 2], "rotation": [0, 0.70710678, 0, 0.70710678] }],
              "meshes": [{ "primitives": [{ "attributes": { "POSITION": 0 } }] }],
              "accessors": [{ "componentType": 5126, "count": 8, "type": "VEC3", "min": [-1, 0, -0.5], "max": [1, 3, 1.5] }]
            }
            """;
        string path = Path.Combine(_directory, "Turned.gltf");
        File.WriteAllText(path, Gltf);

        ModelBounds bounds = KitBounds.Measure(path);

        Assert.Equal(0.5f, bounds.MinX, 1e-4f);
        Assert.Equal(2.5f, bounds.MaxX, 1e-4f);
        Assert.Equal(0f, bounds.MinY, 1e-4f);
        Assert.Equal(3f, bounds.MaxY, 1e-4f);
        Assert.Equal(1f, bounds.MinZ, 1e-4f);
        Assert.Equal(3f, bounds.MaxZ, 1e-4f);
    }

    /// <summary>
    /// The box comes from the vertices, not the accessor's min and max: some kit exports leave those stale (the nature
    /// kit's Fern_1 claimed 9 m across for a 2.8 m fern). Two vertices, (-1, 0, -0.5) and (1, 2, 1.5), behind a header
    /// that claims ±5.
    /// </summary>
    [Fact]
    public void Measure_a_model_from_its_vertices_when_its_accessor_box_is_stale()
    {
        const string Gltf = """
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [{ "nodes": [0] }],
              "nodes": [{ "mesh": 0 }],
              "meshes": [{ "primitives": [{ "attributes": { "POSITION": 0 } }] }],
              "buffers": [{ "byteLength": 24, "uri": "data:application/octet-stream;base64,AACAvwAAAAAAAAC/AACAPwAAAEAAAMA/" }],
              "bufferViews": [{ "buffer": 0, "byteLength": 24 }],
              "accessors": [{ "bufferView": 0, "componentType": 5126, "count": 2, "type": "VEC3", "min": [-5, -5, -5], "max": [5, 5, 5] }]
            }
            """;
        string path = Path.Combine(_directory, "Stale.gltf");
        File.WriteAllText(path, Gltf);

        Assert.Equal(new ModelBounds(-1f, 1f, 0f, 2f, -0.5f, 1.5f), KitBounds.Measure(path));
    }
}
