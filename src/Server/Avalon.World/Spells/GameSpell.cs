using Avalon.Common.ValueObjects;
using Avalon.World.Public.Spells;

namespace Avalon.World.Spells;

public class GameSpell : ISpell
{
    public required AbilityId AbilityId { get; init; }

    public required SpellMetadata Metadata { get; init; }
    public required float CooldownTimer { get; set; }
    public required float CastTimeTimer { get; set; }
    public bool Casting { get; set; }

    public ISpell Clone()
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
