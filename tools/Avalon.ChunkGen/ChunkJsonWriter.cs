using System.Globalization;
using System.Text;
using Avalon.Database.World.Seeding;

namespace Avalon.ChunkGen;

/// <summary>
/// Writes a chunk's catalog entry, the seeder's own <see cref="ChunkMetaDto" />, in the exact layout of the committed
/// catalog files. Portal slots are not written: no generated piece has one.
/// </summary>
public static class ChunkJsonWriter
{
    public static string Write(ChunkMetaDto meta)
    {
        if (meta.PortalSlots.Count > 0)
            throw new InvalidOperationException($"{meta.Name}: generated pieces carry no portal slots");

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"name\": \"").Append(meta.Name).Append("\",\n");
        sb.Append("  \"assetKey\": \"").Append(meta.AssetKey).Append("\",\n");
        sb.Append("  \"cellFootprintX\": ").Append(meta.CellFootprintX).Append(",\n");
        sb.Append("  \"cellFootprintZ\": ").Append(meta.CellFootprintZ).Append(",\n");
        sb.Append("  \"cellSize\": ").Append(F(meta.CellSize)).Append(",\n");
        sb.Append("  \"exits\": {\n");
        string[] sides = ["N", "E", "S", "W"];
        for (int i = 0; i < sides.Length; i++)
        {
            string[] slots = meta.Exits.TryGetValue(sides[i], out string[]? s) ? s : [];
            sb.Append("    \"").Append(sides[i]).Append("\": [")
              .Append(string.Join(", ", slots.Select(x => $"\"{x}\""))).Append(']')
              .Append(i < sides.Length - 1 ? ",\n" : "\n");
        }
        sb.Append("  },\n");
        sb.Append("  \"spawnSlots\": [\n");
        for (int i = 0; i < meta.SpawnSlots.Count; i++)
        {
            SpawnSlotDto slot = meta.SpawnSlots[i];
            sb.Append("    { \"tag\": \"").Append(slot.Tag).Append("\", \"localX\": ").Append(F(slot.LocalX))
              .Append(", \"localY\": ").Append(F(slot.LocalY)).Append(", \"localZ\": ").Append(F(slot.LocalZ)).Append(" }")
              .Append(i < meta.SpawnSlots.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ],\n");
        sb.Append("  \"portalSlots\": [\n  ],\n");
        sb.Append("  \"tags\": [").Append(string.Join(", ", meta.Tags.Select(t => $"\"{t}\""))).Append("]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
