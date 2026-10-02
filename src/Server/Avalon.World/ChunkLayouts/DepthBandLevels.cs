using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// Which levels a procedural piece's creatures roll from (forest content pass): the band its depth falls in, or, for a
/// set piece, the highest band (largest MinDepth). The boss stands at the highest band's MaxLevel. Null means "no band":
/// the creature rolls from its template's own range, as on a map with no bands. Pure.
/// </summary>
public static class DepthBandLevels
{
    public static LevelRange? For(IReadOnlyList<ProceduralDepthBand> bands, int depth, bool setPiece)
    {
        if (bands.Count == 0)
            return null;

        if (setPiece)
        {
            ProceduralDepthBand top = Highest(bands);
            return new LevelRange(top.MinLevel, top.MaxLevel);
        }

        foreach (ProceduralDepthBand band in bands)
        {
            if (depth >= band.MinDepth && (band.MaxDepth is null || depth <= band.MaxDepth))
                return new LevelRange(band.MinLevel, band.MaxLevel);
        }

        return null;
    }

    public static ushort? BossLevel(IReadOnlyList<ProceduralDepthBand> bands) =>
        bands.Count == 0 ? null : Highest(bands).MaxLevel;

    /// <summary>
    /// Why a map's bands cannot be used, or null: levels from 1 and not backwards, depths not overlapping, only the highest
    /// band open-ended. A gap between bands is allowed: a depth in it rolls from the creature template's own range, as
    /// <see cref="For" /> answers null there.
    /// </summary>
    public static string? Problem(IReadOnlyList<ProceduralDepthBand> bands)
    {
        List<ProceduralDepthBand> ordered = bands.OrderBy(b => b.MinDepth).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ProceduralDepthBand band = ordered[i];
            if (band.MinDepth < 0)
                return $"band from depth {band.MinDepth} starts below 0";
            if (band.MinLevel < 1 || band.MaxLevel < band.MinLevel)
                return $"band from depth {band.MinDepth} has levels {band.MinLevel}-{band.MaxLevel}";
            if (band.MaxDepth is { } max && max < band.MinDepth)
                return $"band from depth {band.MinDepth} ends at depth {max}";
            if (i + 1 < ordered.Count)
            {
                if (band.MaxDepth is null)
                    return $"band from depth {band.MinDepth} has no end but band from depth {ordered[i + 1].MinDepth} follows it";
                if (band.MaxDepth >= ordered[i + 1].MinDepth)
                    return $"bands from depth {band.MinDepth} and {ordered[i + 1].MinDepth} overlap";
            }
        }

        return null;
    }

    private static ProceduralDepthBand Highest(IReadOnlyList<ProceduralDepthBand> bands) => bands.MaxBy(b => b.MinDepth)!;
}
