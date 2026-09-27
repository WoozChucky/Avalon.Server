using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

public class AbilityTemplate : IDbEntity<AbilityId>
{
    [Key]
    public AbilityId Id { get; set; }

    public string Name { get; set; }

    public uint CastTime { get; set; } // in milliseconds

    public uint Cooldown { get; set; } // in milliseconds

    public uint Cost { get; set; } // in power points

    public string SpellScript { get; set; }

    public SpellRange Range { get; set; } // in meters

    public SpellEffect Effects { get; set; }

    public uint EffectValue { get; set; }

    public List<CharacterClass> AllowedClasses { get; set; } = []; // Default to no classes

    [Required] public float ThreatMultiplier { get; set; } = 1.0f;

    [Required] public float HealThreatPerHp { get; set; } = 0.0f;

    [Required] public uint TauntDurationMs { get; set; } = 0;

    [Required] public AbilityFlags Flags { get; set; } = AbilityFlags.None;

    [Required] public uint AnimationId { get; set; } = 0;

    // Aim and shape (#164). All non-null with defaults so the rows that predate them still load.

    [Required] public AbilityAimMode AimMode { get; set; } = AbilityAimMode.Movement;

    [Required] public AbilityShape Shape { get; set; } = AbilityShape.Circle;

    /// <summary>Circle only: centred on the caster or on the aim point.</summary>
    [Required] public AbilityAnchor Anchor { get; set; } = AbilityAnchor.Caster;

    /// <summary>
    /// Metres. The cursor clamp for an AimPoint circle, a cone's length, a projectile's maximum
    /// travel; 0 for a circle on the caster. Replaces <see cref="Range" />, which new code does not read.
    /// </summary>
    [Required] public float Reach { get; set; }

    /// <summary>Metres. A circle's radius; 0 otherwise.</summary>
    [Required] public float Radius { get; set; }

    /// <summary>Degrees. A cone's full arc; 0 otherwise.</summary>
    [Required] public float ArcDegrees { get; set; }

    /// <summary>Metres per second. Projectile only; 0 otherwise.</summary>
    [Required] public float ProjectileSpeed { get; set; }

    /// <summary>Projectile only: false ends at the first hit, true hits each unit once and flies on.</summary>
    [Required] public bool Pierce { get; set; }

    [Required] public AbilityAffects Affects { get; set; } = AbilityAffects.Hostile;

    /// <summary>
    /// Power the caster gains, in its own pool and capped at its maximum, per unit this ability
    /// damages (#526). 0 or more; 0 gains nothing.
    /// </summary>
    [Required] public int PowerGainPerHit { get; set; }

    // Damage scaling (#506): EffectValue + ScalingCoefficient x the ScalingStat + WeaponCoefficient x a
    // main-hand weapon roll. A heal scales the same way.

    /// <summary>Which derived damage stat the coefficient multiplies: AttackDamage or AbilityDamage.</summary>
    [Required] public ScalingStat ScalingStat { get; set; } = ScalingStat.Attack;

    /// <summary>Finite and 0 or more; 0 adds nothing.</summary>
    [Required] public float ScalingCoefficient { get; set; }

    /// <summary>Finite and 0 or more; 0 rolls no weapon.</summary>
    [Required] public float WeaponCoefficient { get; set; }
}
