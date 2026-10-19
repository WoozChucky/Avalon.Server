using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Avalon.ChunkGen;

/// <summary>One solid part of a custom forest model, in the model's own space at yaw 0, standing up to <paramref name="Top" />.</summary>
public abstract record FootprintPart(string Part, float Top);

/// <summary>A box part: the axis-aligned box a <see cref="BoxBlocker" /> covers once turned by a quarter turn.</summary>
public sealed record BoxPart(string Part, float MinX, float MaxX, float MinZ, float MaxZ, float Top) : FootprintPart(Part, Top);

/// <summary>
/// A round part: a circle of <paramref name="Radius" />, which a twelve-sided <see cref="CylinderBlocker" /> of
/// <paramref name="BlockerRadius" /> covers (its flat sides reach the circle: Radius / cos 15°, rounded up).
/// </summary>
public sealed record CirclePart(string Part, float X, float Z, float Radius, float BlockerRadius, float Top) : FootprintPart(Part, Top);

/// <summary>
/// The custom forest models' solid footprints (Kit/forest-footprints.json): for each avalon_* model of the forest
/// props, the parts a player collides with, measured in Blender with the models themselves (the art's
/// footprints.json, which never enters the repository). A walk-through model (the dirt paths) has none. The
/// "kit-bounds" command copies them in with the bounds table, so the two stay in step (ChunkGenCli).
/// </summary>
public static class ForestFootprints
{
    /// <summary>The art's file in the custom forest models' glTF directory.</summary>
    public const string ArtFile = "footprints.json";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<FootprintPart>>> s_committed = new(
        () => Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Kit", "forest-footprints.json"))), isThreadSafe: true);

    /// <summary>The committed table, copied next to the assembly at build time.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<FootprintPart>> Load() => s_committed.Value;

    /// <summary>
    /// The table from the art's footprints.json or the committed copy: <c>{ "&lt;model&gt;": { "walkThrough", "solid": [ { "part",
    /// "kind": "box" (minX, maxX, minZ, maxZ) or "circle" (x, z, radius, blockerRadius), "top" } ] } }</c>; other fields
    /// are ignored. A model that is walk-through yet has solid parts, or solid with none, an unknown kind, a missing or
    /// non-finite number, or an empty or inverted shape is refused (InvalidDataException naming the model).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<FootprintPart>> Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var table = new Dictionary<string, IReadOnlyList<FootprintPart>>(StringComparer.Ordinal);
        foreach (JsonProperty model in document.RootElement.EnumerateObject())
        {
            try
            {
                bool walkThrough = model.Value.GetProperty("walkThrough").GetBoolean();
                List<FootprintPart> parts = model.Value.TryGetProperty("solid", out JsonElement solid)
                    ? [.. solid.EnumerateArray().Select(ReadPart).OrderBy(p => p.Part, StringComparer.Ordinal)]
                    : [];
                if (walkThrough == parts.Count > 0)
                    throw new InvalidDataException(walkThrough ? "is walk-through but has solid parts" : "is solid but has no solid part");
                table.Add(model.Name, parts);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw new InvalidDataException($"{model.Name}: {ex.Message}", ex);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"{model.Name} {ex.Message}", ex);
            }
        }
        return table;
    }

    /// <summary>The table as committed: models in ordinal order, one part per line, numbers with at most 3 decimals, LF line ends.</summary>
    public static string Write(IReadOnlyDictionary<string, IReadOnlyList<FootprintPart>> table)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        string[] names = [.. table.Keys.Order(StringComparer.Ordinal)];
        for (int i = 0; i < names.Length; i++)
        {
            IReadOnlyList<FootprintPart> parts = table[names[i]];
            sb.Append("  \"").Append(JsonEncodedText.Encode(names[i])).Append("\": { \"walkThrough\": ")
              .Append(parts.Count == 0 ? "true" : "false").Append(", \"solid\": [");
            for (int p = 0; p < parts.Count; p++)
            {
                sb.Append(p == 0 ? "\n" : ",\n").Append("    { \"part\": ").Append(JsonSerializer.Serialize(parts[p].Part));
                switch (parts[p])
                {
                    case BoxPart box:
                        sb.Append(", \"kind\": \"box\", \"minX\": ").Append(F(box.MinX)).Append(", \"maxX\": ").Append(F(box.MaxX))
                          .Append(", \"minZ\": ").Append(F(box.MinZ)).Append(", \"maxZ\": ").Append(F(box.MaxZ));
                        break;
                    case CirclePart circle:
                        sb.Append(", \"kind\": \"circle\", \"x\": ").Append(F(circle.X)).Append(", \"z\": ").Append(F(circle.Z))
                          .Append(", \"radius\": ").Append(F(circle.Radius)).Append(", \"blockerRadius\": ").Append(F(circle.BlockerRadius));
                        break;
                }
                sb.Append(", \"top\": ").Append(F(parts[p].Top)).Append(" }");
            }
            sb.Append(parts.Count == 0 ? "] }" : "\n  ] }").Append(i < names.Length - 1 ? ",\n" : "\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    private static FootprintPart ReadPart(JsonElement part)
    {
        string name = part.GetProperty("part").GetString() ?? throw new InvalidDataException("has a part without a name");
        float top = Number(part, "top");
        FootprintPart read = part.GetProperty("kind").GetString() switch
        {
            "box" => new BoxPart(name, Number(part, "minX"), Number(part, "maxX"), Number(part, "minZ"), Number(part, "maxZ"), top),
            "circle" => new CirclePart(name, Number(part, "x"), Number(part, "z"), Number(part, "radius"), Number(part, "blockerRadius"), top),
            string kind => throw new InvalidDataException($"part {name} has the unknown kind '{kind}'"),
            null => throw new InvalidDataException($"part {name} has no kind"),
        };
        bool shaped = read switch
        {
            BoxPart b => b.MinX < b.MaxX && b.MinZ < b.MaxZ,
            CirclePart c => c.Radius > 0f && c.BlockerRadius >= c.Radius,
            _ => false,
        };
        if (!shaped || top <= 0f) throw new InvalidDataException($"part {name} is empty or inverted: {Describe(read)}");
        return read;
    }

    /// <summary>A part's numbers in the invariant culture, for a refusal.</summary>
    private static string Describe(FootprintPart part) => part switch
    {
        BoxPart b => $"box x {F(b.MinX)}..{F(b.MaxX)}, z {F(b.MinZ)}..{F(b.MaxZ)}, top {F(b.Top)}",
        CirclePart c => $"circle at ({F(c.X)}, {F(c.Z)}), radius {F(c.Radius)}, blockerRadius {F(c.BlockerRadius)}, top {F(c.Top)}",
        _ => part.Part,
    };

    private static float Number(JsonElement part, string name)
    {
        float value = part.GetProperty(name).GetSingle();
        return float.IsFinite(value) ? value : throw new InvalidDataException($"has a {name} that is not a finite number");
    }

    /// <summary>Rounded to 3 decimals, with a value that rounds to zero written as 0, never -0.</summary>
    private static string F(float value)
    {
        double rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
