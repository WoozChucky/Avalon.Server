using System.Globalization;
using System.Text;
using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// Deterministic fingerprint of the inputs that drive procedural generation.
///
/// A seed only reproduces a layout while the config and chunk pool behind it are
/// unchanged. Editing a pool weight or swapping a geometry file silently changes what
/// an old seed generates. Both the world server (at instance creation) and Avalon.Api
/// (when serving a preview) compute this from the same code, so a mismatch tells the
/// admin SPA that the geometry it is about to draw may not match the player's client.
///
/// FNV-1a over a canonical text encoding. Pool members are ordered by ChunkTemplateId
/// before hashing so DB row order cannot change the result. Floats use round-trip
/// ("R") invariant formatting so the encoding is stable across platforms.
/// </summary>
public static class LayoutConfigVersion
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    public static string Compute(ProceduralMapConfig config, IReadOnlyList<ChunkPoolMember> pool,
        IReadOnlyList<ChunkGroupDefinition>? groups = null)
    {
        var sb = new StringBuilder();

        sb.Append(config.MapTemplateId.Value.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append(config.ChunkPoolId.Value.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append(config.SpawnTableId.Value.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append(config.MainPathMin.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append(config.MainPathMax.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append(F(config.BranchChance)).Append('|')
          .Append(config.BranchMaxDepth.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append(config.HasBoss ? '1' : '0').Append('|')
          .Append(config.BackPortalTargetMapId.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append(config.ForwardPortalTargetMapId?.ToString(CultureInfo.InvariantCulture) ?? "-")
          .Append(';');

        // Appended only when set, so a config without it keeps its fingerprint.
        if (config.MinSetPieceStep > 0)
            sb.Append("minSetPieceStep:").Append(config.MinSetPieceStep.ToString(CultureInfo.InvariantCulture)).Append(';');

        foreach (ChunkPoolMember m in pool.OrderBy(p => p.Template.Id.Value))
        {
            sb.Append(m.Template.Id.Value.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(F(m.Weight)).Append('|')
              .Append(m.Template.GeometryFile).Append('|')
              .Append(m.Template.CellFootprintX.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(m.Template.CellFootprintZ.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(F(m.Template.CellSize)).Append('|')
              .Append(m.Template.Exits.ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        // Set pieces (forest content pass). Appended only when there are any, so a pool without them keeps its
        // fingerprint. Ordered by name and cell so database row order cannot change the result.
        if (groups is { Count: > 0 })
        {
            sb.Append("groups;");
            foreach (ChunkGroupDefinition g in groups.OrderBy(g => g.Name, StringComparer.Ordinal))
            {
                sb.Append(g.Name).Append(':');
                foreach (ChunkGroupCell c in g.Cells.OrderBy(c => c.CellZ).ThenBy(c => c.CellX))
                {
                    sb.Append(c.CellX.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(c.CellZ.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(c.Template.Id.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(c.Template.GeometryFile).Append(',')
                      .Append(c.Template.Exits.ToString(CultureInfo.InvariantCulture)).Append(';');
                }
            }
        }

        return Fnv1a(sb.ToString()).ToString("x8", CultureInfo.InvariantCulture);
    }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static uint Fnv1a(string text)
    {
        uint hash = FnvOffsetBasis;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return hash;
    }
}
