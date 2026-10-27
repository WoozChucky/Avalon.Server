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

    public AbilityMetadata Clone() =>
        new()
        {
            Name = Name,
            CastTime = CastTime,
            Cooldown = Cooldown,
            Cost = Cost,
            ScriptName = ScriptName,
            Range = Range,
            Effects = Effects,
            ThreatMultiplier = ThreatMultiplier,
            HealThreatPerHp = HealThreatPerHp,
            TauntDurationMs = TauntDurationMs,
            Flags = Flags,
            AnimationId = AnimationId,
            AimMode = AimMode,
            Shape = Shape,
            Anchor = Anchor,
            Reach = Reach,
            Radius = Radius,
            ArcDegrees = ArcDegrees,
            ProjectileSpeed = ProjectileSpeed,
            Pierce = Pierce,
            MaxTargets = MaxTargets,
            Affects = Affects,
            DamageType = DamageType,
            Tags = Tags,
            DamageEffectiveness = DamageEffectiveness,
            SpellBaseMin = SpellBaseMin,
            SpellBaseMax = SpellBaseMax,
            SpellBasePerLevelPct = SpellBasePerLevelPct,
            ConvertToType = ConvertToType,
            ConvertPct = ConvertPct,
            AddedType = AddedType,
            AddedMin = AddedMin,
            AddedMax = AddedMax,
        };
}
