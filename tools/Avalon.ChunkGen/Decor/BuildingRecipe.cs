using System.Text.Json;

namespace Avalon.ChunkGen;

/// <summary>
/// A building as kit pieces plus the simple shapes the navmesh bakes for it, every coordinate relative to the building's
/// anchor (0, 0, 0). Exactly one proxy carries the building's <see cref="TownPiece.Front" />. The proxies' and pieces'
/// <c>Building</c> is empty here and filled in when <see cref="TownBuilding.Place" /> puts the building in a square.
/// </summary>
public sealed record BuildingRecipe(string Id, IReadOnlyList<DecorItem> Pieces, IReadOnlyList<TownPiece> Proxies)
{
    /// <summary>The fields each proxy kind takes besides "kind", "part", "material" and "front" (a box also "walkable").</summary>
    private static readonly Dictionary<string, string[]> s_fields = new(StringComparer.Ordinal)
    {
        ["box"] = ["minX", "maxX", "minZ", "maxZ", "y0", "top"],
        ["gable"] = ["minX", "maxX", "minZ", "maxZ", "eaves", "ridge"],
        ["cylinder"] = ["x", "z", "radius", "y0", "top"],
        ["ring"] = ["x", "z", "radius", "innerRadius", "y0", "top"],
    };

    /// <summary>
    /// Reads Recipes/&lt;id&gt;.json, copied next to the assembly at build time:
    /// <c>{ "id", "pieces": [ { "model", "x", "y", "z", "yaw" } ], "proxies": [ { "kind", "part", ...the shape's fields } ] }</c>.
    /// A missing or unknown field, an id that is not the file's, a kind or front it does not know, a material other
    /// than "collision" (the only one a proxy is written with), or a "walkable" on anything but a box is refused
    /// (InvalidDataException).
    /// </summary>
    public static BuildingRecipe Load(string id)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Recipes", id + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        Only(root, path, "id", "pieces", "proxies");
        if (ReadString(root, "id", path) != id)
            throw new InvalidDataException($"{path}: id '{ReadString(root, "id", path)}' is not the file's name");

        List<DecorItem> pieces = [];
        foreach (JsonElement piece in ReadArray(root, "pieces", path))
        {
            Only(piece, path, "model", "x", "y", "z", "yaw");
            pieces.Add(new DecorItem(string.Empty, ReadString(piece, "model", path),
                ReadNumber(piece, "x", path), ReadNumber(piece, "y", path), ReadNumber(piece, "z", path), ReadInt(piece, "yaw", path)));
        }

        List<TownPiece> proxies = [];
        foreach (JsonElement proxy in ReadArray(root, "proxies", path))
            proxies.Add(Proxy(proxy, path));

        return new BuildingRecipe(id, pieces, proxies);
    }

    private static TownPiece Proxy(JsonElement proxy, string path)
    {
        string kind = ReadString(proxy, "kind", path);
        if (!s_fields.TryGetValue(kind, out string[]? fields))
            throw new InvalidDataException($"{path}: proxy kind '{kind}' is none of box, gable, cylinder, ring");
        string[] common = kind == "box" ? ["kind", "part", "material", "front", "walkable"] : ["kind", "part", "material", "front"];
        Only(proxy, path, [.. common, .. fields]);

        string part = ReadString(proxy, "part", path);
        if (proxy.TryGetProperty("material", out _) && ReadString(proxy, "material", path) != Materials.Name(Material.Collision))
        {
            throw new InvalidDataException(
                $"{path}: proxy '{part}' has material '{ReadString(proxy, "material", path)}', but a recipe's proxies are only " +
                "ever collision (decor draws the building): leave it out or write \"collision\"");
        }

        float N(string field) => ReadNumber(proxy, field, path);
        TownPiece piece = kind switch
        {
            "box" => new BoxPiece(string.Empty, part, Material.Collision, N("minX"), N("maxX"), N("minZ"), N("maxZ"), N("y0"), N("top"),
                proxy.TryGetProperty("walkable", out JsonElement walkable) ? ReadBool(walkable, "walkable", path) : false),
            "gable" => new GablePiece(string.Empty, part, Material.Collision, N("minX"), N("maxX"), N("minZ"), N("maxZ"), N("eaves"), N("ridge")),
            "cylinder" => new CylinderPiece(string.Empty, part, Material.Collision, N("x"), N("z"), N("radius"), N("y0"), N("top")),
            _ => new RingPiece(string.Empty, part, Material.Collision, N("x"), N("z"), N("radius"), N("innerRadius"), N("y0"), N("top")),
        };

        if (!proxy.TryGetProperty("front", out _)) return piece;
        string front = ReadString(proxy, "front", path);
        return front switch
        {
            "NegX" => piece with { Front = Facing.NegX },
            "PosX" => piece with { Front = Facing.PosX },
            "NegZ" => piece with { Front = Facing.NegZ },
            "PosZ" => piece with { Front = Facing.PosZ },
            _ => throw new InvalidDataException($"{path}: proxy '{part}' has front '{front}', none of NegX, PosX, NegZ, PosZ"),
        };
    }

    private static void Only(JsonElement element, string path, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{path}: expected an object, found {element.ValueKind}");
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidDataException($"{path}: unknown field '{property.Name}'");
        }
    }

    private static JsonElement Field(JsonElement element, string name, string path, JsonValueKind kind) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == kind
            ? value
            : throw new InvalidDataException($"{path}: '{name}' is missing or not a {kind}");

    private static string ReadString(JsonElement element, string name, string path) =>
        Field(element, name, path, JsonValueKind.String).GetString()!;

    private static float ReadNumber(JsonElement element, string name, string path) =>
        Field(element, name, path, JsonValueKind.Number).GetSingle();

    private static int ReadInt(JsonElement element, string name, string path) =>
        Field(element, name, path, JsonValueKind.Number).TryGetInt32(out int value)
            ? value
            : throw new InvalidDataException($"{path}: '{name}' is not a whole number");

    private static bool ReadBool(JsonElement value, string name, string path) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new InvalidDataException($"{path}: '{name}' is not true or false"),
    };

    private static JsonElement.ArrayEnumerator ReadArray(JsonElement element, string name, string path) =>
        Field(element, name, path, JsonValueKind.Array).EnumerateArray();
}
