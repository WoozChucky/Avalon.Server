using Avalon.Common;

namespace Avalon.World.Abilities;

/// <summary>
/// The units one projectile cast has hit (#1181 Part D), so a unit is affected at most once per cast: taken from the
/// instance's <see cref="AbilityScriptPool" /> by the cast's first projectile, and shared by the lanes of a fan, each
/// counted in <see cref="Lanes" />. The last lane to let go of it hands it back, emptied.
/// </summary>
internal sealed class ProjectileHits
{
    public HashSet<ObjectGuid> Units { get; } = [];

    /// <summary>The projectiles flying with it: the set goes back to its pool when the last one lets go.</summary>
    public int Lanes { get; set; }
}
