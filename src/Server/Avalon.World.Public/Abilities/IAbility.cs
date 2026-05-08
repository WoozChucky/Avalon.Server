using Avalon.Common.ValueObjects;

namespace Avalon.World.Public.Abilities;

public interface IAbility
{
    AbilityId AbilityId { get; init; }
    AbilityMetadata Metadata { get; }
    float CooldownTimer { get; set; } // in seconds (remaining time until spell is ready after being cast)
    float CastTimeTimer { get; set; } // in seconds (remaining time until spell is cast)
    bool Casting { get; set; }

    IAbility Clone();
}
