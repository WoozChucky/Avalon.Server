using Avalon.Common.ValueObjects;

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
