using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Abilities;

/// <summary>
/// The lanes of a fan (#1181 Part D, the <c>AddedProjectiles</c> capstone): a projectile ability whose
/// <see cref="AbilityMetadata.ProjectileCount" /> is above 1 fires that many projectiles, <see cref="SpreadDegrees" />
/// apart and centred on the aim. Lane 0 is the one furthest anticlockwise seen from above (the lowest yaw), the last the
/// one furthest clockwise; an odd count has a lane on the aim itself.
/// </summary>
internal static class FanAim
{
    /// <summary>The angle between neighbouring lanes, in degrees.</summary>
    public const float SpreadDegrees = 10f;

    /// <summary>
    /// Lane <paramref name="lane" /> of <paramref name="count" />: the aim's direction from its origin, turned about Y by
    /// (<paramref name="lane" /> − (<paramref name="count" /> − 1) / 2) × <see cref="SpreadDegrees" />, as a facing with no
    /// point and the aim's origin. For a count of 1, the aim as it is. A struct in, a struct out: nothing is allocated.
    /// </summary>
    public static AbilityAim Lane(AbilityAim aim, int lane, int count)
    {
        if (count <= 1)
            return aim;

        Vector3 direction = aim.DirectionFrom(aim.Origin ?? Vector3.zero);
        double radians = (lane - (count - 1) / 2.0) * SpreadDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        // A turn by yaw (0 along +Z, 90 along +X, as AbilityAim.FacingFromYaw reads it).
        var turned = new Vector3((float)(direction.x * cos + direction.z * sin), 0f,
            (float)(direction.z * cos - direction.x * sin));
        return new AbilityAim(turned, null) { Origin = aim.Origin };
    }
}
