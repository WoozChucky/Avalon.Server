using System.Text.Json;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// A map's ring file, <c>Maps/Scenery/&lt;mapTemplateId&gt;.json</c>: either a fixed list of placements (the town) or a
/// pool the ring is generated from per layout (the forest); exactly one of the two is set.
/// </summary>
public sealed record SceneryRingFile(ushort MapTemplateId, IReadOnlyList<SceneryPlacement>? Placements, SceneryPool? Pool);

/// <summary>
/// Reads a ring file, as ChunkGen writes it. Two shapes, every field required and no other allowed:
/// <code>
/// { "mapTemplateId": 1, "placements": [ { "name": "town_ring_m1_m1", "gridX": -1, "gridZ": -1, "rotation": 0 } ] }
/// { "mapTemplateId": 2, "pool": [
///   { "name": "forest_cap_rockfall_01", "kind": "cap", "weight": 1, "capped": ["S"] },
///   { "name": "forest_ring_woods_01", "kind": "border", "weight": 3, "front": "S", "filler": true },
///   { "name": "forest_townward_0_m1", "kind": "townward", "spot": [0, -1] } ] }
/// </code>
/// A cap's <c>capped</c> lists its capped sides in its own frame as side letters; a border's <c>front</c> is one side
/// letter. Placements refuse a rotation outside 0-3, two placements on one cell and a name that is not
/// <c>[a-z0-9_]+</c>; a pool is validated by <see cref="SceneryPool" />. Every refusal is an
/// <see cref="InvalidDataException" /> whose message starts with <c>where</c> (the file's path).
/// </summary>
public static class SceneryFile
{
    public static SceneryRingFile Read(string json, string where)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{where}: not a JSON document: {e.Message}", e);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            Only(root, where, "mapTemplateId", "placements", "pool");
            ushort mapTemplateId = Field(root, "mapTemplateId", where, JsonValueKind.Number).TryGetUInt16(out ushort id)
                ? id
                : throw new InvalidDataException($"{where}: 'mapTemplateId' is not a map template id (0-65535)");

            bool hasPlacements = root.TryGetProperty("placements", out _);
            bool hasPool = root.TryGetProperty("pool", out _);
            if (hasPlacements == hasPool)
                throw new InvalidDataException($"{where}: a ring file holds exactly one of 'placements' and 'pool'");

            return hasPlacements
                ? new SceneryRingFile(mapTemplateId, ReadPlacements(root, where), null)
                : new SceneryRingFile(mapTemplateId, null, ReadPool(root, where));
        }
    }

    private static List<SceneryPlacement> ReadPlacements(JsonElement root, string where)
    {
        List<SceneryPlacement> placements = [];
        HashSet<(int X, int Z)> cells = [];
        int index = 0;
        foreach (JsonElement entry in ReadArray(root, "placements", where))
        {
            string at = $"{where}: placements[{index++}]";
            Only(entry, at, "name", "gridX", "gridZ", "rotation");
            string name = ReadName(entry, at);
            int gridX = ReadInt(entry, "gridX", at);
            int gridZ = ReadInt(entry, "gridZ", at);
            int rotation = ReadInt(entry, "rotation", at);
            if (rotation is < 0 or > 3)
                throw new InvalidDataException($"{at}: '{name}' has rotation {rotation}, not 0-3");
            if (!cells.Add((gridX, gridZ)))
                throw new InvalidDataException($"{at}: '{name}' is a second placement on cell ({gridX}, {gridZ})");

            placements.Add(new SceneryPlacement(name, gridX, gridZ, rotation));
        }

        return placements;
    }

    private static SceneryPool ReadPool(JsonElement root, string where)
    {
        List<SceneryPoolPiece> pieces = [];
        int index = 0;
        foreach (JsonElement entry in ReadArray(root, "pool", where))
        {
            string at = $"{where}: pool[{index++}]";
            if (entry.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{at}: expected an object, found {entry.ValueKind}");

            string kind = ReadString(entry, "kind", at);
            pieces.Add(kind switch
            {
                "border" => ReadBorder(entry, at),
                "cap" => ReadCap(entry, at),
                "townward" => ReadTownWard(entry, at),
                _ => throw new InvalidDataException($"{at}: kind '{kind}' is none of border, cap, townward"),
            });
        }

        try
        {
            return new SceneryPool(pieces);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{where}: {e.Message}", e);
        }
    }

    private static SceneryPoolPiece ReadBorder(JsonElement entry, string at)
    {
        Only(entry, at, "name", "kind", "weight", "front", "filler");
        return new SceneryPoolPiece(ReadName(entry, at), SceneryKind.Border, ReadNumber(entry, "weight", at),
            ReadSide(Field(entry, "front", at, JsonValueKind.String), "front", at), 0, ReadBool(entry, "filler", at), 0, 0);
    }

    private static SceneryPoolPiece ReadCap(JsonElement entry, string at)
    {
        Only(entry, at, "name", "kind", "weight", "capped");
        byte capped = 0;
        foreach (JsonElement letter in ReadArray(entry, "capped", at))
        {
            byte bit = ScenerySides.Bit(ReadSide(letter, "capped", at));
            if ((capped & bit) != 0)
                throw new InvalidDataException($"{at}: 'capped' lists side {ScenerySides.Name(bit)} twice");
            capped |= bit;
        }

        return new SceneryPoolPiece(ReadName(entry, at), SceneryKind.Cap, ReadNumber(entry, "weight", at), default, capped, false, 0, 0);
    }

    private static SceneryPoolPiece ReadTownWard(JsonElement entry, string at)
    {
        Only(entry, at, "name", "kind", "spot");
        JsonElement spot = Field(entry, "spot", at, JsonValueKind.Array);
        if (spot.GetArrayLength() != 2
            || !(spot[0].ValueKind == JsonValueKind.Number && spot[0].TryGetInt32(out int spotX))
            || !(spot[1].ValueKind == JsonValueKind.Number && spot[1].TryGetInt32(out int spotZ)))
        {
            throw new InvalidDataException($"{at}: 'spot' is not two whole numbers [x, z]");
        }

        return new SceneryPoolPiece(ReadName(entry, at), SceneryKind.TownWard, 0, default, 0, false, spotX, spotZ);
    }

    private static string ReadName(JsonElement entry, string at)
    {
        string name = ReadString(entry, "name", at);
        return SceneryPool.IsPlainName(name)
            ? name
            : throw new InvalidDataException($"{at}: '{name}' is not a plain piece name ([a-z0-9_]+)");
    }

    private static ExitSide ReadSide(JsonElement value, string name, string at) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() switch
            {
                "N" => ExitSide.N,
                "E" => ExitSide.E,
                "S" => ExitSide.S,
                "W" => ExitSide.W,
                string other => throw new InvalidDataException($"{at}: '{name}' has side '{other}', none of N, E, S, W"),
                null => throw new InvalidDataException($"{at}: '{name}' is not a side"),
            }
            : throw new InvalidDataException($"{at}: '{name}' is not a side letter (a string), found {value.ValueKind}");

    private static void Only(JsonElement element, string at, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{at}: expected an object, found {element.ValueKind}");

        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidDataException($"{at}: unknown field '{property.Name}'");
            if (!seen.Add(property.Name))
                throw new InvalidDataException($"{at}: field '{property.Name}' is written twice");
        }
    }

    private static JsonElement Field(JsonElement element, string name, string at, JsonValueKind kind) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == kind
            ? value
            : throw new InvalidDataException($"{at}: '{name}' is missing or not a {kind}");

    private static string ReadString(JsonElement element, string name, string at) =>
        Field(element, name, at, JsonValueKind.String).GetString()!;

    private static float ReadNumber(JsonElement element, string name, string at) =>
        Field(element, name, at, JsonValueKind.Number).TryGetSingle(out float value)
            ? value
            : throw new InvalidDataException($"{at}: '{name}' is not a number a float holds");

    private static int ReadInt(JsonElement element, string name, string at) =>
        Field(element, name, at, JsonValueKind.Number).TryGetInt32(out int value)
            ? value
            : throw new InvalidDataException($"{at}: '{name}' is not a whole number");

    private static bool ReadBool(JsonElement element, string name, string at) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidDataException($"{at}: '{name}' is not true or false"),
            }
            : throw new InvalidDataException($"{at}: '{name}' is missing");

    private static JsonElement.ArrayEnumerator ReadArray(JsonElement element, string name, string at) =>
        Field(element, name, at, JsonValueKind.Array).EnumerateArray();
}
