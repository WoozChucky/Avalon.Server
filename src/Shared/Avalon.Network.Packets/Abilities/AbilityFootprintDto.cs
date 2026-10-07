using Avalon.Common.Mathematics;
using Avalon.Network.Packets.World;
using ProtoBuf;

namespace Avalon.Network.Packets.Abilities;

/// <summary>
/// Where an ability lands, as the server resolved it (#648): enough to draw it without knowing the caster's
/// abilities. Sent on <c>SUnitStartCastPacket</c> for a cast-time cast, so a client can draw the telegraph
/// for the whole cast, and on <c>SAbilityFiredPacket</c> when a circle or a cone fires. The server resolves
/// it once, from where the caster stood and what it aimed at when the cast started, and the cast fires with
/// exactly this footprint, so the telegraph is never corrected. Heights are drawn from the ground; every hit
/// test is on X/Z.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Circle:</b> <see cref="Centre" /> and <see cref="Radius" />. The centre is the caster's position
/// (anchor Caster) or the aim point clamped to the ability's reach and pulled back from any wall between
/// (anchor AimPoint).</item>
/// <item><b>Cone:</b> <see cref="Origin" />, <see cref="Direction" /> (a unit vector on X/Z),
/// <see cref="Reach" /> (its length) and <see cref="ArcDegrees" /> (its full angle). Walls do not clip it.</item>
/// <item><b>Projectile:</b> a lane from <see cref="Origin" /> along <see cref="Direction" /> for
/// <see cref="Reach" />, cut where the walkable ray stops; the projectile may stop earlier on its first hit. It
/// is sent on the start only: once fired, the projectile is a world object with its own replication.</item>
/// </list>
/// A member a shape does not use is absent (the vectors) or 0 (the floats).
/// </remarks>
[ProtoContract]
public class AbilityFootprintDto
{
    [ProtoMember(1)] public AbilityShape Shape { get; set; }

    /// <summary>
    /// The caster's position when the cast started. Always sent; not initialised, so a decoded footprint re-encodes
    /// exactly as it arrived.
    /// </summary>
    [ProtoMember(2)] public Vector3Dto? Origin { get; set; }

    /// <summary>A cone's or a projectile's direction, a unit vector on X/Z; absent for a circle.</summary>
    [ProtoMember(3)] public Vector3Dto? Direction { get; set; }

    /// <summary>A circle's centre; absent for a cone and a projectile.</summary>
    [ProtoMember(4)] public Vector3Dto? Centre { get; set; }

    /// <summary>A circle's radius, in metres; 0 for the other shapes.</summary>
    [ProtoMember(5)] public float Radius { get; set; }

    /// <summary>A cone's length, or a projectile lane's, in metres; 0 for a circle.</summary>
    [ProtoMember(6)] public float Reach { get; set; }

    /// <summary>A cone's full angle, in degrees; 0 for the other shapes.</summary>
    [ProtoMember(7)] public float ArcDegrees { get; set; }

    public static AbilityFootprintDto Create(AbilityShape shape, Vector3 origin, Vector3? direction, Vector3? centre,
        float radius, float reach, float arcDegrees) => new()
        {
            Shape = shape,
            Origin = Vector3Dto.From(origin),
            Direction = direction is { } d ? Vector3Dto.From(d) : null,
            Centre = centre is { } c ? Vector3Dto.From(c) : null,
            Radius = radius,
            Reach = reach,
            ArcDegrees = arcDegrees,
        };
}
