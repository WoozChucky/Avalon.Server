using Avalon.Common.ValueObjects;

namespace Avalon.World.Public.Spells;

public interface ISpell
{
    SpellId SpellId { get; init; }
    
    float Cooldown { get; } // in seconds
    float CooldownTimer { get; set; } // in seconds (remaining time until spell is ready after being cast)
    
    float CastTime { get; } // in seconds
    float CastTimeTimer { get; set; } // in seconds (remaining time until spell is cast)
    uint PowerCost { get; }
}
