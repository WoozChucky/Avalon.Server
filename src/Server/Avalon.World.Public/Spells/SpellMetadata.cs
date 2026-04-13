// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Spells;

public class SpellMetadata
{
    public string Name { get; init; }

    public float CastTime { get; init; } // in milliseconds

    public float Cooldown { get; init; } // in milliseconds

    public uint Cost { get; init; } // in power points
    public string ScriptName { get; init; }
    public SpellRange Range { get; init; } // in meters

    public SpellEffect Effects { get; init; }

    public uint EffectValue { get; init; }

    public SpellMetadata Clone() =>
        new()
        {
            Name = Name,
            CastTime = CastTime,
            Cooldown = Cooldown,
            Cost = Cost,
            ScriptName = ScriptName,
            Range = Range,
            Effects = Effects,
            EffectValue = EffectValue
        };
}
