namespace Avalon.Domain.World;

/// <summary>
/// The fixed numbers of Awakening (#1180), code and not configuration, so the world server and the REST API agree without
/// reading the world's settings.
/// </summary>
public static class ItemAwakening
{
    /// <summary>How many item levels above the copy's own a tier rise may reach: a target tier's required item level is at most the copy's plus this.</summary>
    public const int TierReach = 3;

    /// <summary>
    /// The points a sealed affix's bar needs to wake on a copy of <paramref name="itemLevel" />: 50 at item level 1, ten
    /// more per level after it. Nothing caps the item level it reads.
    /// </summary>
    public static int Threshold(ushort itemLevel) => 40 + 10 * itemLevel;
}
