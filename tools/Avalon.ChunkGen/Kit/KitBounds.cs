using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Avalon.ChunkGen;

/// <summary>A model's axis-aligned box in its own space, in metres, as the client draws it before placing it.</summary>
public sealed record ModelBounds(float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ);

/// <summary>
/// The art kit's bounds table (Kit/kit-bounds.json): each model's box, keyed by its glTF file name without the
/// extension, so pieces can be checked against the art without the kit itself, which never enters the repository.
/// The "kit-bounds" command measures the kit's glTF files and rewrites the table (ChunkGenCli).
/// </summary>
public static class KitBounds
{
    /// <summary>Every bound is required: a model missing one is refused rather than read as 0.</summary>
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true };

    /// <summary>The committed table, copied next to the assembly at build time. A model missing a bound is refused (JsonException).</summary>
    public static IReadOnlyDictionary<string, ModelBounds> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Kit", "kit-bounds.json");
        Dictionary<string, ModelBounds> table = JsonSerializer.Deserialize<Dictionary<string, ModelBounds>>(File.ReadAllText(path), s_options)
            ?? throw new InvalidDataException($"{path} holds no table");
        return new Dictionary<string, ModelBounds>(table, StringComparer.Ordinal);
    }

    /// <summary>
    /// The box of every primitive's POSITION vertices carried through the node transforms of the file's scene and
    /// joined. The box is measured from the vertices themselves (float VEC3, from the buffer the accessor's bufferView
    /// points into: a file beside the glTF or a base64 data URI), not from the accessor's min and max, which some kit
    /// exports leave stale (the nature kit's Fern_1 and Plant_1_Big state boxes several times their meshes); an accessor
    /// with no bufferView holds no data, so its min and max are its box. A file that is not such a glTF, or whose
    /// positions are not float VEC3 or are sparse, is refused (InvalidDataException naming it).
    /// </summary>
    public static ModelBounds Measure(string gltfPath)
    {
        try
        {
            return MeasureDocument(gltfPath);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException
                                       or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{gltfPath} is not a glTF the tool can measure: {ex.Message}", ex);
        }
    }

    private static ModelBounds MeasureDocument(string gltfPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(gltfPath));
        JsonElement root = document.RootElement;
        int scene = root.TryGetProperty("scene", out JsonElement sceneIndex) ? sceneIndex.GetInt32() : 0;
        if (!root.TryGetProperty("scenes", out JsonElement scenes) || scene >= scenes.GetArrayLength())
            throw new InvalidDataException($"{gltfPath} has no scene {scene}");

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        JsonElement nodes = root.GetProperty("nodes");
        var buffers = new Dictionary<int, byte[]>();
        foreach (JsonElement node in scenes[scene].GetProperty("nodes").EnumerateArray())
            Visit(root, gltfPath, buffers, nodes[node.GetInt32()], Matrix4x4.Identity, ref min, ref max);

        if (min.X > max.X) throw new InvalidDataException($"{gltfPath} draws no mesh");
        return new ModelBounds(min.X, max.X, min.Y, max.Y, min.Z, max.Z);
    }

    /// <summary>
    /// The table as committed: one model per line, keys JSON-escaped in ordinal order, values rounded to 3 decimals, LF
    /// line ends. A bound that is not a finite number is refused (ArgumentException naming the model).
    /// </summary>
    public static string Write(IReadOnlyDictionary<string, ModelBounds> table)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        string[] names = [.. table.Keys.Order(StringComparer.Ordinal)];
        for (int i = 0; i < names.Length; i++)
        {
            ModelBounds b = table[names[i]];
            if (!new[] { b.MinX, b.MaxX, b.MinY, b.MaxY, b.MinZ, b.MaxZ }.All(float.IsFinite))
                throw new ArgumentException($"{names[i]} has a bound that is not a finite number: {b}", nameof(table));
            sb.Append("  \"").Append(JsonEncodedText.Encode(names[i])).Append("\": { ")
              .Append("\"minX\": ").Append(F(b.MinX)).Append(", \"maxX\": ").Append(F(b.MaxX))
              .Append(", \"minY\": ").Append(F(b.MinY)).Append(", \"maxY\": ").Append(F(b.MaxY))
              .Append(", \"minZ\": ").Append(F(b.MinZ)).Append(", \"maxZ\": ").Append(F(b.MaxZ)).Append(" }")
              .Append(i < names.Length - 1 ? ",\n" : "\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    private static void Visit(
        JsonElement root, string gltfPath, Dictionary<int, byte[]> buffers, JsonElement node, Matrix4x4 parent, ref Vector3 min, ref Vector3 max)
    {
        Matrix4x4 world = Local(node) * parent;
        if (node.TryGetProperty("mesh", out JsonElement mesh))
        {
            JsonElement accessors = root.GetProperty("accessors");
            foreach (JsonElement primitive in root.GetProperty("meshes")[mesh.GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                JsonElement accessor = accessors[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                (Vector3 lo, Vector3 hi) = PositionBox(root, gltfPath, buffers, accessor);
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3((corner & 1) == 0 ? lo.X : hi.X, (corner & 2) == 0 ? lo.Y : hi.Y, (corner & 4) == 0 ? lo.Z : hi.Z);
                    var placed = Vector3.Transform(point, world);
                    min = Vector3.Min(min, placed);
                    max = Vector3.Max(max, placed);
                }
            }
        }

        if (node.TryGetProperty("children", out JsonElement children))
        {
            JsonElement nodes = root.GetProperty("nodes");
            foreach (JsonElement child in children.EnumerateArray())
                Visit(root, gltfPath, buffers, nodes[child.GetInt32()], world, ref min, ref max);
        }
    }

    /// <summary>
    /// The node's own transform: its column-major "matrix", or scale, then rotation, then translation. System.Numerics
    /// multiplies row vectors, so the column-major array reads straight into its row-major fields.
    /// </summary>
    private static Matrix4x4 Local(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement matrix))
        {
            float[] m = [.. matrix.EnumerateArray().Select(e => e.GetSingle())];
            return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        }

        Matrix4x4 scale = node.TryGetProperty("scale", out JsonElement s) ? Matrix4x4.CreateScale(Vec3(s)) : Matrix4x4.Identity;
        Matrix4x4 rotation = node.TryGetProperty("rotation", out JsonElement r)
            ? Matrix4x4.CreateFromQuaternion(new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()))
            : Matrix4x4.Identity;
        Matrix4x4 translation = node.TryGetProperty("translation", out JsonElement t) ? Matrix4x4.CreateTranslation(Vec3(t)) : Matrix4x4.Identity;
        return scale * rotation * translation;
    }

    /// <summary>A POSITION accessor's box in its mesh's space, from its vertices (see <see cref="Measure" />).</summary>
    private static (Vector3 Min, Vector3 Max) PositionBox(JsonElement root, string gltfPath, Dictionary<int, byte[]> buffers, JsonElement accessor)
    {
        if (!accessor.TryGetProperty("bufferView", out JsonElement viewIndex))
            return (Vec3(accessor.GetProperty("min")), Vec3(accessor.GetProperty("max")));
        if (accessor.TryGetProperty("sparse", out _))
            throw new InvalidDataException($"{gltfPath} has a sparse POSITION accessor");
        if (accessor.GetProperty("componentType").GetInt32() != 5126 || accessor.GetProperty("type").GetString() != "VEC3")
            throw new InvalidDataException($"{gltfPath} has POSITION data that is not float VEC3");

        JsonElement view = root.GetProperty("bufferViews")[viewIndex.GetInt32()];
        int bufferIndex = view.GetProperty("buffer").GetInt32();
        if (!buffers.TryGetValue(bufferIndex, out byte[]? data))
        {
            string uri = root.GetProperty("buffers")[bufferIndex].GetProperty("uri").GetString()
                ?? throw new InvalidDataException($"{gltfPath} has a buffer without a uri");
            int comma = uri.IndexOf(',', StringComparison.Ordinal);
            data = uri.StartsWith("data:", StringComparison.Ordinal) && comma > 0
                ? Convert.FromBase64String(uri[(comma + 1)..])
                : File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(gltfPath)!, Uri.UnescapeDataString(uri)));
            buffers[bufferIndex] = data;
        }

        int offset = (view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0)
            + (accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0);
        int stride = view.TryGetProperty("byteStride", out JsonElement st) ? st.GetInt32() : 12;
        int count = accessor.GetProperty("count").GetInt32();
        if (count <= 0 || offset + (count - 1) * stride + 12 > data.Length)
            throw new InvalidDataException($"{gltfPath} has POSITION data outside its buffer");

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> v = data.AsSpan(offset + i * stride, 12);
            var p = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(v), BinaryPrimitives.ReadSingleLittleEndian(v[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(v[8..]));
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return (min, max);
    }

    private static Vector3 Vec3(JsonElement array) => new(array[0].GetSingle(), array[1].GetSingle(), array[2].GetSingle());

    /// <summary>Rounded to 3 decimals, with a value that rounds to zero written as 0, never -0.</summary>
    private static string F(float value)
    {
        double rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
