using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalon.World.ChunkLayouts;

namespace Avalon.ChunkGen;

/// <summary>
/// Writes a map's ring file (<c>Maps/Scenery/&lt;mapTemplateId&gt;.json</c>) in the shape SceneryFile.Read reads: the
/// forest's pool or the town's placements, one entry per line, 2-space indent, numbers invariant with at most 3
/// decimals, LF line ends, so a rerun is byte-identical.
/// </summary>
public static class SceneryJsonWriter
{
    /// <summary>
    /// A pool, sorted by name: a border's weight, front and filler flag, a cap's weight and capped sides (letters in N,
    /// E, S, W order), a town-ward piece's spot. Refuses (InvalidOperationException naming the piece) a town-ward
    /// piece with no spot, a cap that lists a side twice and a side that is none of N, E, S, W.
    /// </summary>
    public static string WritePool(ushort mapId, IReadOnlyList<SceneryPiece> pieces)
    {
        List<string> entries = [];
        foreach (SceneryPiece piece in pieces.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var entry = new StringBuilder();
            entry.Append("{ \"name\": ").Append(JsonSerializer.Serialize(piece.Name));
            switch (piece.Kind)
            {
                case SceneryKind.Border:
                    entry.Append(", \"kind\": \"border\", \"weight\": ").Append(F(piece.Weight))
                         .Append(", \"front\": \"").Append(Letter(piece, piece.Front)).Append('"')
                         .Append(", \"filler\": ").Append(piece.Filler ? "true" : "false");
                    break;
                case SceneryKind.Cap:
                    if (piece.Capped.GroupBy(s => s).FirstOrDefault(g => g.Count() > 1) is { } twice)
                        throw new InvalidOperationException($"{piece.Name}: caps side {Letter(piece, twice.Key)} twice");
                    entry.Append(", \"kind\": \"cap\", \"weight\": ").Append(F(piece.Weight)).Append(", \"capped\": [")
                         .Append(string.Join(", ", piece.Capped.Order().Select(s => $"\"{Letter(piece, s)}\""))).Append(']');
                    break;
                case SceneryKind.TownWard:
                    (int X, int Z) spot = piece.Spot ?? throw new InvalidOperationException($"{piece.Name}: a town-ward piece without a spot");
                    entry.Append(", \"kind\": \"townward\", \"spot\": [").Append(I(spot.X)).Append(", ").Append(I(spot.Z)).Append(']');
                    break;
                default:
                    throw new InvalidOperationException($"{piece.Name}: kind {piece.Kind} has no pool entry");
            }
            entries.Add(entry.Append(" }").ToString());
        }
        return Document(mapId, "pool", entries);
    }

    /// <summary>
    /// The town's placements: every piece on its cell (GridX, GridZ), rotation 0, sorted by GridX then GridZ. Refuses
    /// (InvalidOperationException naming the piece) a second piece on one cell.
    /// </summary>
    public static string WritePlacements(ushort mapId, IReadOnlyList<SceneryPiece> pieces)
    {
        HashSet<(int X, int Z)> cells = [];
        foreach (SceneryPiece piece in pieces)
        {
            if (!cells.Add((piece.GridX, piece.GridZ)))
                throw new InvalidOperationException($"{piece.Name}: a second placement on cell ({I(piece.GridX)}, {I(piece.GridZ)})");
        }

        return Document(mapId, "placements", [.. pieces.OrderBy(p => p.GridX).ThenBy(p => p.GridZ).Select(p =>
            $"{{ \"name\": {JsonSerializer.Serialize(p.Name)}, \"gridX\": {I(p.GridX)}, \"gridZ\": {I(p.GridZ)}, \"rotation\": 0 }}")]);
    }

    private static string Document(ushort mapId, string list, IReadOnlyList<string> entries)
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"mapTemplateId\": ").Append(I(mapId)).Append(",\n");
        if (entries.Count == 0)
        {
            sb.Append("  \"").Append(list).Append("\": []\n");
        }
        else
        {
            sb.Append("  \"").Append(list).Append("\": [\n");
            for (int i = 0; i < entries.Count; i++)
                sb.Append("    ").Append(entries[i]).Append(i < entries.Count - 1 ? ",\n" : "\n");
            sb.Append("  ]\n");
        }
        return sb.Append("}\n").ToString();
    }

    private static string Letter(SceneryPiece piece, Side side) => side switch
    {
        Side.N => "N",
        Side.E => "E",
        Side.S => "S",
        Side.W => "W",
        _ => throw new InvalidOperationException($"{piece.Name}: side {I((int)side)} is none of N, E, S, W"),
    };

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Rounded to 3 decimals, with a value that rounds to zero written as 0, never -0.</summary>
    private static string F(float value)
    {
        double rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
