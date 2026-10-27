using Avalon.Common.ValueObjects;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Public.Characters;

/// <summary>
/// A character's abilities, read-only: what it holds is its class start's starter and the talent tree's ability nodes it
/// holds (#1181), set World-side, so nothing here grants one.
/// </summary>
public interface ICharacterAbilities
{
    IAbility? this[AbilityId abilityId] { get; }

    bool IsCasting { get; }

    void Update(TimeSpan deltaTime);
}
