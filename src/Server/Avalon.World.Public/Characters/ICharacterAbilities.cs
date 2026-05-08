using Avalon.Common.ValueObjects;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Public.Characters;

public interface ICharacterAbilities
{
    void Load(IReadOnlyCollection<IAbility> spells);

    IAbility? this[AbilityId abilityId] { get; }

    bool IsCasting { get; }

    void Update(TimeSpan deltaTime);
}
