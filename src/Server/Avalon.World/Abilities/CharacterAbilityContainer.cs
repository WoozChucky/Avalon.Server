using Avalon.Common.Mathematics;
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
            // Update ability cooldown timer if not on cooldown and not casting
            if (ability.CooldownTimer > 0 && Mathf.Approximately(ability.CastTimeTimer, ability.Metadata.CastTime))
            {
                ability.CooldownTimer -= (float)deltaTime.TotalSeconds;
            }
        }
    }
}
