using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Avalon.ChunkGen;

/// <summary>
/// Writes a chunk's decor file (Maps/Decor/&lt;chunk&gt;.json), what the client draws from the art kit:
/// <c>{ "chunk", "kit", "items": [ { "model", "x", "y", "z", "yaw", "scale" } ] }</c>, "scale" only when it is not 1,
/// the items sorted by model, then x, z, y, yaw and scale, one per line, numbers in invariant culture with at most 3
/// decimals, 2-space indent, LF line ends, so a rerun on any machine is byte-identical. The server never reads it.
/// </summary>
public static class DecorJsonWriter
{
    /// <summary>A town square's kit: its items turn by quarter turns (<see cref="DecorYaw.QuarterTurns" />).</summary>
    public const string TownKit = "medieval-village-megakit-standard";

    /// <summary>A forest piece's kit: its items turn by any whole degree (<see cref="DecorYaw.AnyDegree" />).</summary>
    public const string ForestKit = "stylized-nature-megakit-standard";

    public static string Write(string chunk, IReadOnlyList<DecorItem> items, string kit)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"chunk\": ").Append(JsonSerializer.Serialize(chunk)).Append(",\n");
        sb.Append("  \"kit\": ").Append(JsonSerializer.Serialize(kit)).Append(",\n");
        if (items.Count == 0)
        {
            sb.Append("  \"items\": []\n");
        }
        else
        {
            sb.Append("  \"items\": [\n");
            DecorItem[] sorted =
            [
                .. items.OrderBy(i => i.Model, StringComparer.Ordinal).ThenBy(i => i.X).ThenBy(i => i.Z).ThenBy(i => i.Y)
                    .ThenBy(i => i.Yaw).ThenBy(i => i.Scale),
            ];
            for (int i = 0; i < sorted.Length; i++)
            {
                DecorItem item = sorted[i];
                sb.Append("    { \"model\": ").Append(JsonSerializer.Serialize(item.Model))
                  .Append(", \"x\": ").Append(F(item.X)).Append(", \"y\": ").Append(F(item.Y)).Append(", \"z\": ").Append(F(item.Z))
                  .Append(", \"yaw\": ").Append(item.Yaw.ToString(CultureInfo.InvariantCulture));
                string scale = F(item.Scale);
                if (scale != "1")
                    sb.Append(", \"scale\": ").Append(scale);
                sb.Append(" }").Append(i < sorted.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>Rounded to 3 decimals, with a value that rounds to zero written as 0, never -0.</summary>
    private static string F(float value)
    {
        double rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
