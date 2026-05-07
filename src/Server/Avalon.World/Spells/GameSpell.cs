using Avalon.Common.ValueObjects;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Spells;

public class GameSpell : IAbility
{
    public required AbilityId AbilityId { get; init; }

    public required AbilityMetadata Metadata { get; init; }
    public required float CooldownTimer { get; set; }
    public required float CastTimeTimer { get; set; }
    public bool Casting { get; set; }

    public IAbility Clone()
    {
        return new GameSpell
        {
            AbilityId = AbilityId,
            Metadata = Metadata.Clone(),
            CooldownTimer = CooldownTimer,
            CastTimeTimer = CastTimeTimer
        };
    }
}
