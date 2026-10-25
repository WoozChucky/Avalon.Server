namespace Avalon.Network.Packets.Combat;

/// <summary>
/// A hit's damage type (#1178): physical, or one of the three elements, or blight. The stored item column
/// (<c>ItemTemplates.DamageType</c>) holds these numbers. Append-only.
/// </summary>
public enum DamageType
{
    Physical = 0,
    Fire = 1,
    Cold = 2,
    Lightning = 3,
    Blight = 4,
}

/// <summary>The damage types a hit carried, one bit per <see cref="DamageType" /> (<c>1 &lt;&lt; type</c>). Append-only.</summary>
[Flags]
public enum DamageTypeMask
{
    None = 0,
    Physical = 1,
    Fire = 2,
    Cold = 4,
    Lightning = 8,
    Blight = 16,
}
