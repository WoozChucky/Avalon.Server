using System.Globalization;
using Avalon.Server.World.UnitTests.Procedural;
using Avalon.World.ChunkLayouts;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

/// <summary>
/// The World server reads every map's ring file at start-up, and a file it cannot read stops the start, naming the
/// file: a map whose ring silently went missing would be drawn without its edge.
/// </summary>
public sealed class SceneryCatalogShould : IDisposable
{
    private const string CapAll =
        """{ "name": "t_cap_all", "kind": "cap", "weight": 1, "capped": ["N", "E", "S", "W"] },""";

    private readonly string _maps =
        Path.Combine(Path.GetTempPath(), $"avalon-scenery-catalog-{Guid.NewGuid():N}", "Maps");

    public void Dispose()
    {
        string root = Path.GetDirectoryName(_maps)!;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    // A row whose rewrite missed would load cleanly and fail the Throws below, so no row can pass by not changing.
    public static TheoryData<string, string, string> UnreadableFiles() => new()
    {
        { "2.json", "{ not json", "2.json" },
        {
            "2.json",
            SceneryTestPool.Json.Replace("\"mapTemplateId\": 2", "\"mapTemplateId\": 3", StringComparison.Ordinal),
            "mapTemplateId"
        },
        {
            "2.json",
            SceneryTestPool.Json.Replace(CapAll, "", StringComparison.Ordinal),
            "no cap for four capped sides"
        },
    };

    /// <summary>
    /// A file that is not JSON, names another map than its own, or holds an incomplete pool is refused.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnreadableFiles))]
    public void Refuse_a_ring_file_it_cannot_read_naming_it(string fileName, string json, string words)
    {
        string scenery = Path.Combine(_maps, "Scenery");
        Directory.CreateDirectory(scenery);
        string path = Path.Combine(scenery, fileName);
        File.WriteAllText(path, json);

        InvalidDataException refusal = Assert.Throws<InvalidDataException>(() => SceneryCatalog.Load(_maps));

        Assert.Contains(path, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(words, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every shipped map ships its ring, of the kind its layout source reads: a fixed map (Maps/TownLayouts) its
    /// placements, a procedural map (Maps/ProceduralMaps) its pool. A map whose ring file is missing, or of the other
    /// kind, would load cleanly and be drawn without its edge.
    /// </summary>
    [Fact]
    public void Hold_a_ring_for_every_map_with_a_layout()
    {
        string maps = Path.Combine(AppContext.BaseDirectory, "Maps");
        var catalog = SceneryCatalog.Load(maps);

        foreach (ushort id in MapIds(maps, "TownLayouts"))
        {
            Assert.NotEmpty(catalog.FixedFor(id));
            Assert.Null(catalog.PoolFor(id));
        }
        foreach (ushort id in MapIds(maps, "ProceduralMaps"))
        {
            Assert.NotNull(catalog.PoolFor(id));
            Assert.Empty(catalog.FixedFor(id));
        }
    }

    private static IEnumerable<ushort> MapIds(string maps, string layouts)
    {
        string[] files = Directory.GetFiles(Path.Combine(maps, layouts), "*.json");
        Assert.NotEmpty(files);
        return files.Select(f => ushort.Parse(Path.GetFileNameWithoutExtension(f), CultureInfo.InvariantCulture));
    }
}
