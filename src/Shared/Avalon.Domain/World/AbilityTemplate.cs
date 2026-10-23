using System.ComponentModel.DataAnnotations;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
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

    /// <summary>
    /// The pool <see cref="Cost" /> is spent from (#652): Mana, Fury or Energy. None only with a cost of 0; a cast is
    /// refused when the caster's pool is a different one, however much it holds.
    /// </summary>
    public PowerType CostPowerType { get; set; }

    public string ScriptName { get; set; }

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

    // Damage scaling (#506): EffectValue + ScalingCoefficient x the ScalingStat + BaseDamageCoefficient x a
    // roll of the caster's base damage: a character's main-hand weapon, a creature's natural DamageMin..DamageMax
    // (#163). A heal scales the same way.

    /// <summary>Which derived damage stat the coefficient multiplies: AttackDamage or AbilityDamage.</summary>
    [Required] public ScalingStat ScalingStat { get; set; } = ScalingStat.Attack;

    /// <summary>Finite and 0 or more; 0 adds nothing.</summary>
    [Required] public float ScalingCoefficient { get; set; }

    /// <summary>Finite and 0 or more; 0 rolls no base damage.</summary>
    [Required] public float BaseDamageCoefficient { get; set; }

    /// <summary>
    /// The aura every unit this ability's shape affects receives from the caster, or null. A Hostile ability applies
    /// only a harmful aura, an Ally one only a helpful aura; the direct amount is applied first, and only when
    /// <see cref="Effects" /> has Damage (Hostile) or Heal (Ally).
    /// </summary>
    public AuraId? AuraId { get; set; }

    /// <summary>
    /// The type of damage the ability deals, and the type its damage-over-time aura takes (#1178), or null: a creature
    /// ability with none deals its creature's base type. An Ally ability names none; a class ability that deals damage,
    /// directly or through a damaging aura, must name one (<c>AbilityRules</c>, <c>AuraRules.DamageTypeProblem</c>).
    /// </summary>
    public DamageType? DamageType { get; set; }

    // The offence model (#1178). An Attack deals its weapon's roll times DamageEffectiveness; a Spell or a Heal its own
    // spell base, grown by the caster's level. AbilityRules holds the rows to the model.

    /// <summary>What the ability is: Attack, Spell or Heal, and its delivery (Area, Projectile, Melee).</summary>
    public AbilityTags Tags { get; set; }

    /// <summary>
    /// Percent: what an Attack's weapon roll, and a Spell's added damage, count for. Finite and 0 or more; 100 counts
    /// them whole.
    /// </summary>
    public float DamageEffectiveness { get; set; } = 100f;

    /// <summary>The lowest a Spell or a Heal rolls at level 1, inclusive; at most <see cref="SpellBaseMax" />.</summary>
    public uint SpellBaseMin { get; set; }

    /// <summary>The highest a Spell or a Heal rolls at level 1, inclusive; above 0 for one that deals or heals.</summary>
    public uint SpellBaseMax { get; set; }

    /// <summary>
    /// Percent of the level-1 spell base added for each level above 1 (linear). Finite and 0 or more.
    /// </summary>
    public float SpellBasePerLevelPct { get; set; }

    /// <summary>The element an Attack converts <see cref="ConvertPct" /> of its weapon's physical damage to, or null.</summary>
    public DamageType? ConvertToType { get; set; }

    /// <summary>Percent of an Attack's physical damage converted to <see cref="ConvertToType" />, 0 to 100.</summary>
    public float ConvertPct { get; set; }

    /// <summary>The type of the ability's built-in added damage, or null for none.</summary>
    public DamageType? AddedType { get; set; }

    /// <summary>The lowest built-in added damage, inclusive; at most <see cref="AddedMax" />.</summary>
    public uint AddedMin { get; set; }

    /// <summary>The highest built-in added damage, inclusive; 0 adds none.</summary>
    public uint AddedMax { get; set; }
}
