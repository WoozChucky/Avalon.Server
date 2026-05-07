// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

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
        };
}
