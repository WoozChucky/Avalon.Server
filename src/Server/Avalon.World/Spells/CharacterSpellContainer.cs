using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Spells;

public class CharacterSpellContainer : ICharacterSpells
{
    private readonly ILogger<CharacterSpellContainer> _logger;
    private IReadOnlyCollection<ISpell> _spells;
    
    public CharacterSpellContainer(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<CharacterSpellContainer>();
    }
    
    public ISpell? this[SpellId spellId] => _spells.FirstOrDefault(x => x.SpellId == spellId);
    
    public bool IsCasting => _spells.Any(x => x.Casting);

    public void Load(IReadOnlyCollection<ISpell> spells)
    {
        _spells = spells;
        _logger.LogInformation("Loading {Count} spells into character", _spells.Count);
    }
    
    public void Update(TimeSpan deltaTime)
    {
        foreach (var spell in _spells)
        {
            // Update spell cooldown timer if not on cooldown and not casting
            if (spell.CooldownTimer > 0 && Mathf.Approximately(spell.CastTimeTimer, spell.Metadata.CastTime))
            {
                spell.CooldownTimer -= (float)deltaTime.TotalSeconds;
            }
        }
    }
}

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
