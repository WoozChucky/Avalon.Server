using Avalon.Common.ValueObjects;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Abilities;

public class CharacterAbilityContainer(ILoggerFactory loggerFactory) : ICharacterAbilities
{
    private readonly ILogger<CharacterAbilityContainer> _logger = loggerFactory.CreateLogger<CharacterAbilityContainer>();
    private IReadOnlyCollection<IAbility> _abilities;

    public IAbility? this[AbilityId abilityId] => _abilities.FirstOrDefault(x => x.AbilityId == abilityId);

    public bool IsCasting => _abilities.Any(x => x.Casting);

    public void Load(IReadOnlyCollection<IAbility> abilities)
    {
        _abilities = abilities;
        _logger.LogInformation("Loading {Count} abilities into character", _abilities.Count);
    }

    public void Update(TimeSpan deltaTime)
    {
        foreach (var ability in _abilities)
        {
            // #627: a cooldown counts down whatever the cast timer holds, which a hasted cast time never
            // matches with the metadata's. An ability being cast has no cooldown running: a cast is refused
            // while it has one, and the cooldown is set only once the cast fires.
            if (ability.CooldownTimer > 0)
            {
                ability.CooldownTimer -= (float)deltaTime.TotalSeconds;
            }
        }
    }
}
