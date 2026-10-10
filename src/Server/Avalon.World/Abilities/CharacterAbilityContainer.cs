using Avalon.Common.ValueObjects;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Abilities;

public class CharacterAbilityContainer(ILoggerFactory loggerFactory) : ICharacterAbilities
{
    private readonly ILogger<CharacterAbilityContainer> _logger = loggerFactory.CreateLogger<CharacterAbilityContainer>();
    private IReadOnlyCollection<IAbility> _abilities;

    // The loaded collection as a list, when it is one (an array, a List, a collection expression): walked by index,
    // so the per-tick Update, the cast handler's lookup and IsCasting allocate no boxed enumerator, closure or
    // delegate (#880). Null for any other collection, which is walked as before.
    private IReadOnlyList<IAbility>? _list;

    /// <summary>Every ability loaded, in load order; empty before the first load. World-side (#669).</summary>
    public IReadOnlyCollection<IAbility> All => _abilities ?? [];

    public IAbility? this[AbilityId abilityId]
    {
        get
        {
            if (_list is not { } list)
                return Find(_abilities, abilityId);

            for (int i = 0; i < list.Count; i++)
            {
                IAbility ability = list[i];
                if (ability.AbilityId == abilityId)
                    return ability;
            }

            return null;
        }
    }

    // Apart from the indexer: a lambda over its parameter there would allocate its closure on every call, the list's
    // included.
    private static IAbility? Find(IReadOnlyCollection<IAbility> abilities, AbilityId abilityId) =>
        abilities.FirstOrDefault(x => x.AbilityId == abilityId);

    public bool IsCasting
    {
        get
        {
            if (_list is not { } list)
                return _abilities.Any(x => x.Casting);

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Casting)
                    return true;
            }

            return false;
        }
    }

    public void Load(IReadOnlyCollection<IAbility> abilities)
    {
        _abilities = abilities;
        _list = abilities as IReadOnlyList<IAbility>;
        _logger.LogInformation("Loading {Count} abilities into character", _abilities.Count);
    }

    public void Update(TimeSpan deltaTime)
    {
        if (_list is { } list)
        {
            for (int i = 0; i < list.Count; i++)
                CountDown(list[i], deltaTime);
            return;
        }

        foreach (IAbility ability in _abilities)
            CountDown(ability, deltaTime);
    }

    private static void CountDown(IAbility ability, TimeSpan deltaTime)
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
