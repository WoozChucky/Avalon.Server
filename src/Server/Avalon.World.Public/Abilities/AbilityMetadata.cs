// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Abilities;

public class AbilityMetadata
{
    public string Name { get; init; }

    public float CastTime { get; init; } // in milliseconds

    public float Cooldown { get; init; } // in milliseconds

    public uint Cost { get; init; } // in power points
    public string ScriptName { get; init; }
    public SpellRange Range { get; init; } // in meters

    public SpellEffect Effects { get; init; }

    public uint EffectValue { get; init; }

    public float        ThreatMultiplier { get; init; } = 1.0f;
    public float        HealThreatPerHp  { get; init; } = 0.0f;
    public uint         TauntDurationMs  { get; init; } = 0;
    public AbilityFlags Flags            { get; init; } = AbilityFlags.None;
    public uint         AnimationId      { get; init; } = 0;

    // Aim and shape (#164), copied from the template.

    /// <summary>Along the caster's facing, or at the cursor's ground point.</summary>
    public AbilityAimMode AimMode         { get; init; } = AbilityAimMode.Movement;

    /// <summary>Circle, cone or projectile.</summary>
    public AbilityShape   Shape           { get; init; } = AbilityShape.Circle;

    /// <summary>Circle only: centred on the caster or on the aim point.</summary>
    public AbilityAnchor  Anchor          { get; init; } = AbilityAnchor.Caster;

    /// <summary>Metres: aim point clamp, cone length or projectile travel; 0 for a circle on the caster.</summary>
    public float          Reach           { get; init; }

    /// <summary>Metres. A circle's radius; 0 otherwise.</summary>
    public float          Radius          { get; init; }

    /// <summary>Degrees. A cone's full arc; 0 otherwise.</summary>
    public float          ArcDegrees      { get; init; }

    /// <summary>Metres per second. Projectile only; 0 otherwise.</summary>
    public float          ProjectileSpeed { get; init; }

    /// <summary>Projectile only: false ends at the first hit, true hits each unit once and flies on.</summary>
    public bool           Pierce          { get; init; }

    /// <summary>Hostile units are damaged, allies are healed.</summary>
    public AbilityAffects Affects         { get; init; } = AbilityAffects.Hostile;

    /// <summary>Power the caster gains per unit this ability damages (#526); 0 gains nothing.</summary>
    public int            PowerGainPerHit { get; init; }

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
            EffectValue = EffectValue,
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
            Affects = Affects,
            PowerGainPerHit = PowerGainPerHit,
        };
}
