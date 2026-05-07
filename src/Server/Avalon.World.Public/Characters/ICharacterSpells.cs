using Avalon.Common.ValueObjects;
using Avalon.World.Public.Spells;

namespace Avalon.World.Public.Characters;

public interface ICharacterSpells
{
    void Load(IReadOnlyCollection<ISpell> spells);

    ISpell? this[AbilityId abilityId] { get; }

    bool IsCasting { get; }

    void Update(TimeSpan deltaTime);
}
