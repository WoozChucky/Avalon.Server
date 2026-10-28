using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Abilities;

/// <summary>
/// An ability's row as the world runs it. It cannot change once built, and is sealed so no subclass can add what
/// can: one object is shared by a caster's ability and by the copy each of its casts hands its script (#925).
/// </summary>
public sealed class AbilityMetadata
{
    public string Name { get; init; }

    public float CastTime { get; init; } // in seconds (the template stores milliseconds; the mapper divides by 1000)

    public float Cooldown { get; init; } // in seconds (the template stores milliseconds; the mapper divides by 1000)

    public uint Cost { get; init; } // in mana
    public string ScriptName { get; init; }
    public SpellRange Range { get; init; } // in meters

    public SpellEffect Effects { get; init; }

    public float ThreatMultiplier { get; init; } = 1.0f;
    public float HealThreatPerHp { get; init; } = 0.0f;
    public uint TauntDurationMs { get; init; } = 0;
    public AbilityFlags Flags { get; init; } = AbilityFlags.None;
    public uint AnimationId { get; init; } = 0;

    // Aim and shape (#164), copied from the template.

    /// <summary>From the caster toward the cursor, else along its facing (Movement, #716), or at the cursor's ground point (Cursor).</summary>
    public AbilityAimMode AimMode { get; init; } = AbilityAimMode.Movement;

    /// <summary>Circle, cone or projectile.</summary>
    public AbilityShape Shape { get; init; } = AbilityShape.Circle;

    /// <summary>Circle only: centred on the caster or on the aim point.</summary>
    public AbilityAnchor Anchor { get; init; } = AbilityAnchor.Caster;

    /// <summary>Metres: aim point clamp, cone length or projectile travel; 0 for a circle on the caster.</summary>
    public float Reach { get; init; }

    /// <summary>Metres. A circle's radius; 0 otherwise.</summary>
    public float Radius { get; init; }

    /// <summary>Degrees. A cone's full arc; 0 otherwise.</summary>
    public float ArcDegrees { get; init; }

    /// <summary>Metres per second. Projectile only; 0 otherwise.</summary>
    public float ProjectileSpeed { get; init; }

    /// <summary>Projectile only: false ends at the first hit, true hits each unit once and flies on.</summary>
    public bool Pierce { get; init; }

    /// <summary>
    /// The most units a circle or a cone affects, and the hit a piercing projectile ends on (#1181): the nearest it may
    /// affect, so a unit it may not (an ally of a hostile ability, an unflagged player, or a unit every hit ignores: an
    /// invulnerable creature, a corpse, a creature walking home, a protected arrival) uses up none. 0 is no limit.
    /// </summary>
    public int MaxTargets { get; init; }

    /// <summary>Hostile units are damaged, allies are healed.</summary>
    public AbilityAffects Affects { get; init; } = AbilityAffects.Hostile;

    /// <summary>
    /// The type of damage the ability deals (#1178), or null: the caster's base type (a creature's template's, a
    /// character's Physical). Heals, buffs and pure control name none.
    /// </summary>
    public DamageType? DamageType { get; init; }

    // The offence model (#1178), copied from the template.

    /// <summary>What the ability is: Attack, Spell or Heal, and its delivery (Area, Projectile, Melee).</summary>
    public AbilityTags Tags { get; init; }

    /// <summary>Percent an Attack's weapon roll, and a Spell's added damage, count for; 100 counts them whole.</summary>
    public float DamageEffectiveness { get; init; } = 100f;

    /// <summary>The lowest a Spell or a Heal rolls at level 1, inclusive.</summary>
    public uint SpellBaseMin { get; init; }

    /// <summary>The highest a Spell or a Heal rolls at level 1, inclusive.</summary>
    public uint SpellBaseMax { get; init; }

    /// <summary>Percent of the level-1 spell base added for each level above 1.</summary>
    public float SpellBasePerLevelPct { get; init; }

    /// <summary>The element an Attack converts part of its weapon's physical damage to, or null.</summary>
    public DamageType? ConvertToType { get; init; }

    /// <summary>Percent of an Attack's physical damage converted to <see cref="ConvertToType" />.</summary>
    public float ConvertPct { get; init; }

    /// <summary>The type of the ability's built-in added damage, or null for none.</summary>
    public DamageType? AddedType { get; init; }

    /// <summary>The lowest built-in added damage, inclusive.</summary>
    public uint AddedMin { get; init; }

    /// <summary>The highest built-in added damage, inclusive; 0 adds none.</summary>
    public uint AddedMax { get; init; }

    // Shaped by the capstones a character holds (#1181 Part D), never by the template: an unshaped ability carries the
    // defaults. Reach, Radius and ArcDegrees above carry a capstone's cone and circle changes.

    /// <summary>Projectile only: how many fly, in a fan 10 degrees apart; 1 otherwise.</summary>
    public int ProjectileCount { get; init; } = 1;

    /// <summary>Metres. Projectile only: the circle hit where it ends on a unit; 0 for none.</summary>
    public float ExplosionRadius { get; init; }

    /// <summary>Percent of the ability's amount the explosion hits for.</summary>
    public float ExplosionEffectiveness { get; init; }

    /// <summary>Percent of the amount each affected unit is hit, or healed, again at once; 0 for no second hit.</summary>
    public float SecondHitEffectiveness { get; init; }

    /// <summary>How many stacks above its own cap the caster's aura may reach.</summary>
    public uint AuraStacksBonus { get; init; }

    /// <summary>Percent the caster's aura lasts longer.</summary>
    public float AuraDurationPct { get; init; }

    public AbilityMetadata()
    {
    }

    /// <summary>
    /// A copy of every member of <paramref name="source" />: the one copy list (<c>AbilityMetadataShould</c>), which a
    /// shaped copy starts from and overrides with an initializer.
    /// </summary>
    public AbilityMetadata(AbilityMetadata source)
    {
        Name = source.Name;
        CastTime = source.CastTime;
        Cooldown = source.Cooldown;
        Cost = source.Cost;
        ScriptName = source.ScriptName;
        Range = source.Range;
        Effects = source.Effects;
        ThreatMultiplier = source.ThreatMultiplier;
        HealThreatPerHp = source.HealThreatPerHp;
        TauntDurationMs = source.TauntDurationMs;
        Flags = source.Flags;
        AnimationId = source.AnimationId;
        AimMode = source.AimMode;
        Shape = source.Shape;
        Anchor = source.Anchor;
        Reach = source.Reach;
        Radius = source.Radius;
        ArcDegrees = source.ArcDegrees;
        ProjectileSpeed = source.ProjectileSpeed;
        Pierce = source.Pierce;
        MaxTargets = source.MaxTargets;
        Affects = source.Affects;
        DamageType = source.DamageType;
        Tags = source.Tags;
        DamageEffectiveness = source.DamageEffectiveness;
        SpellBaseMin = source.SpellBaseMin;
        SpellBaseMax = source.SpellBaseMax;
        SpellBasePerLevelPct = source.SpellBasePerLevelPct;
        ConvertToType = source.ConvertToType;
        ConvertPct = source.ConvertPct;
        AddedType = source.AddedType;
        AddedMin = source.AddedMin;
        AddedMax = source.AddedMax;
        ProjectileCount = source.ProjectileCount;
        ExplosionRadius = source.ExplosionRadius;
        ExplosionEffectiveness = source.ExplosionEffectiveness;
        SecondHitEffectiveness = source.SecondHitEffectiveness;
        AuraStacksBonus = source.AuraStacksBonus;
        AuraDurationPct = source.AuraDurationPct;
    }

    public AbilityMetadata Clone() => new(this);
}
