namespace Avalon.World.Abilities;

/// <summary>
/// A projectile that can fly as one lane of a fan (#1181 Part D, the <c>AddedProjectiles</c> capstone): the cast system
/// joins each extra lane to the cast's first projectile before it is prepared, so every lane of the cast shares one set
/// of the units it hit, and a unit is affected at most once per cast.
/// </summary>
internal interface IProjectileFan
{
    /// <summary>
    /// Joins <paramref name="first" />'s set of hit units, one lane more flying with it (<see cref="ProjectileHits.Lanes" />),
    /// in place of a set of its own. Only before this projectile's <c>Prepare</c>, and after <paramref name="first" />'s.
    /// </summary>
    void ShareHitsWith(IProjectileFan first);
}
