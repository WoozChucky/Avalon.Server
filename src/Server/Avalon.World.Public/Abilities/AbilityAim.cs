using Avalon.Common.Mathematics;

namespace Avalon.World.Public.Abilities;

/// <summary>
/// What a cast aims at, captured when the cast starts (#164). <see cref="Facing" /> is a unit vector
/// on X/Z: the direction a Movement skill aims. For a character's cast that sent a cursor point it points
/// from the caster toward that point (#716), otherwise it is the caster's yaw; a creature's points at its
/// target. <see cref="Point" /> is the cursor's ground point for a Cursor skill and null for a Movement
/// skill. Heights are ignored: every shape test is 2D.
/// </summary>
public readonly record struct AbilityAim(Vector3 Facing, Vector3? Point)
{
    private const double MinAimDistance = 0.001;

    /// <summary>
    /// Where the caster stood when the cast started (#648), set by the cast system when it takes the cast. A
    /// shape resolves from here, not from where the caster stands when it fires, so a cast lands where its
    /// telegraph was drawn. Null only for an aim no cast system took.
    /// </summary>
    public Vector3? Origin { get; init; }

    /// <summary>
    /// A yaw in degrees (Y up, 0 along +Z, 90 along +X) as a unit vector on X/Z. A non-finite yaw
    /// aims along +Z: the client's yaw reaches the server unchecked.
    /// </summary>
    public static Vector3 FacingFromYaw(float yawDegrees)
    {
        if (!float.IsFinite(yawDegrees))
        {
            return new Vector3(0f, 0f, 1f);
        }

        double radians = yawDegrees * Math.PI / 180.0;
        return new Vector3((float)Math.Sin(radians), 0f, (float)Math.Cos(radians));
    }

    /// <summary>
    /// The unit X/Z direction from <paramref name="origin" /> toward <see cref="Point" />, or
    /// <see cref="Facing" /> when there is no point or it sits on the origin. Computed in double, so a
    /// far point does not overflow.
    /// </summary>
    public Vector3 DirectionFrom(Vector3 origin) => Point is { } point ? Toward(origin, point, Facing) : Facing;

    /// <summary>
    /// The unit X/Z direction from <paramref name="origin" /> toward <paramref name="point" />, or
    /// <paramref name="fallback" /> when the point sits on the origin (closer than a millimetre) or the distance
    /// is not finite. Computed in double, so a far point does not overflow.
    /// </summary>
    public static Vector3 Toward(Vector3 origin, Vector3 point, Vector3 fallback)
    {
        double dx = (double)point.x - origin.x;
        double dz = (double)point.z - origin.z;
        double length = Math.Sqrt(dx * dx + dz * dz);

        return length < MinAimDistance || !double.IsFinite(length)
            ? fallback
            : new Vector3((float)(dx / length), 0f, (float)(dz / length));
    }
}
