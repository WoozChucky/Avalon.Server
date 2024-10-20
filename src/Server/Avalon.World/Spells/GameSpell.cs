using Avalon.Common.ValueObjects;
using Avalon.World.Public.Spells;

namespace Avalon.World.Spells;

public class GameSpell : ISpell
{
    public required SpellId SpellId { get; init; }
    
    public required SpellMetadata Metadata { get; init; }
    public required float CooldownTimer { get; set; }
    public required float CastTimeTimer { get; set; }
    public bool Casting { get; set; }

    public ISpell Clone()
    {
        return new GameSpell
        {
            SpellId = SpellId,
            Metadata = Metadata.Clone(),
            CooldownTimer = CooldownTimer,
            CastTimeTimer = CastTimeTimer
        };
    }
}