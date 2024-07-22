using Avalon.Common.ValueObjects;
using Avalon.World.Public.Characters;
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
    
    public void Load(IReadOnlyCollection<ISpell> spells)
    {
        _spells = spells;
        _logger.LogInformation("Loading {Count} spells into character", _spells.Count);
    }

    public ISpell? this[SpellId spellId] => _spells.FirstOrDefault(x => x.SpellId == spellId);
}

public class GameSpell : ISpell
{
    public required SpellId SpellId { get; init; }
    
    public required float Cooldown { get; init; }
    public required float CooldownTimer { get; set; }
    
    public required float CastTime { get; init; }
    public required float CastTimeTimer { get; set; }
    public required uint PowerCost { get; init; }
    public required string ScriptName { get; init; }
}
