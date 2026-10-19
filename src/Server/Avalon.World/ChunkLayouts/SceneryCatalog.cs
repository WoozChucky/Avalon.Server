using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// Every map's scenery ring file, read once at start-up: a town's fixed placements, or the pool a forest's ring is
/// built from for each layout (#1110). The ring is drawn by the client and never walked, so nothing here reaches the
/// navmesh.
/// </summary>
public interface ISceneryCatalog
{
    /// <summary>The map's fixed placements; empty for a map with none.</summary>
    IReadOnlyList<SceneryPlacement> FixedFor(ushort mapId);

    /// <summary>The pool the map's ring is built from per layout, or null for a map with none.</summary>
    SceneryPool? PoolFor(ushort mapId);
}

public sealed class SceneryCatalog : ISceneryCatalog
{
    private readonly IReadOnlyDictionary<ushort, SceneryRingFile> _rings;

    public SceneryCatalog(IReadOnlyDictionary<ushort, SceneryRingFile> rings)
    {
        _rings = rings;
    }

    /// <summary>A catalog with no ring for any map.</summary>
    public static SceneryCatalog Empty { get; } = new(new Dictionary<ushort, SceneryRingFile>());

    /// <summary>
    /// Reads every <c>&lt;mapsDirectory&gt;/Scenery/&lt;n&gt;.json</c> whose <c>n</c> is a map template id
    /// (0-65535); the piece files beside them are not ring files and are skipped. A file that does not read, or whose
    /// <c>mapTemplateId</c> is not its name, is refused with an <see cref="InvalidDataException" /> naming its path.
    /// No <c>Scenery</c> directory is <see cref="Empty" />.
    /// </summary>
    [SuppressMessage("Performance", "MA0045",
        Justification = "Read once at start-up, before any instance is built; the ring files are a few KB each.")]
    public static SceneryCatalog Load(string mapsDirectory)
    {
        string directory = Path.Combine(mapsDirectory, "Scenery");
        if (!Directory.Exists(directory))
            return Empty;

        var rings = new Dictionary<ushort, SceneryRingFile>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!ushort.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out ushort mapId))
                continue;

            SceneryRingFile ring = SceneryFile.Read(File.ReadAllText(path), path);
            if (ring.MapTemplateId != mapId)
            {
                string written = ring.MapTemplateId.ToString(CultureInfo.InvariantCulture);
                string named = mapId.ToString(CultureInfo.InvariantCulture);
                throw new InvalidDataException(
                    $"{path}: 'mapTemplateId' is {written}, not the map {named} its name gives");
            }

            if (!rings.TryAdd(mapId, ring))
            {
                throw new InvalidDataException(
                    $"{path}: a second ring file for map {mapId.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        return new SceneryCatalog(rings);
    }

    public IReadOnlyList<SceneryPlacement> FixedFor(ushort mapId) =>
        _rings.TryGetValue(mapId, out SceneryRingFile? ring) && ring.Placements is { } placements ? placements : [];

    public SceneryPool? PoolFor(ushort mapId) =>
        _rings.TryGetValue(mapId, out SceneryRingFile? ring) ? ring.Pool : null;
}
