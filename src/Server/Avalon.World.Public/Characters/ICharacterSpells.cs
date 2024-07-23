using Avalon.Common.ValueObjects;
using Avalon.World.Public.Spells;

namespace Avalon.World.Public.Characters;

public interface ICharacterSpells
{
    void Load(IReadOnlyCollection<ISpell> spells);
    
    ISpell? this[SpellId spellId] { get; }
    
    void Update(TimeSpan deltaTime);
}
