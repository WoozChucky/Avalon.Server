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

    public static string Compute(ProceduralMapConfig config, IReadOnlyList<ChunkPoolMember> pool)
    {
        var sb = new StringBuilder();

        sb.Append(config.MapTemplateId.Value).Append('|')
          .Append(config.ChunkPoolId.Value).Append('|')
          .Append(config.SpawnTableId.Value).Append('|')
          .Append(config.MainPathMin).Append('|')
          .Append(config.MainPathMax).Append('|')
          .Append(F(config.BranchChance)).Append('|')
          .Append(config.BranchMaxDepth).Append('|')
          .Append(config.HasBoss ? '1' : '0').Append('|')
          .Append(config.BackPortalTargetMapId).Append('|')
          .Append(config.ForwardPortalTargetMapId?.ToString(CultureInfo.InvariantCulture) ?? "-")
          .Append(';');

        foreach (ChunkPoolMember m in pool.OrderBy(p => p.Template.Id.Value))
        {
            sb.Append(m.Template.Id.Value).Append('|')
              .Append(F(m.Weight)).Append('|')
              .Append(m.Template.GeometryFile).Append('|')
              .Append(m.Template.CellFootprintX).Append('|')
              .Append(m.Template.CellFootprintZ).Append('|')
              .Append(F(m.Template.CellSize)).Append('|')
              .Append(m.Template.Exits).Append(';');
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
