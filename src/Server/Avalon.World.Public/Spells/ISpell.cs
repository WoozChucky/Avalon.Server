using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Spells;

public interface ISpell
{
    SpellId SpellId { get; init; }
    SpellMetadata Metadata { get; }
    float CooldownTimer { get; set; } // in seconds (remaining time until spell is ready after being cast)
    float CastTimeTimer { get; set; } // in seconds (remaining time until spell is cast)
    bool Casting { get; set; }
    
    ISpell Clone();
}

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
    
    public SpellMetadata Clone()
    {
        return new SpellMetadata
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
}
