using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Characters;

public interface ICharacter : IGameEntity<ulong>
{
    ICharacterInventory this[InventoryType type] { get; }
    
    ICharacterSpells Spells { get; }
    
    uint ChunkId { get; set; }
    
    string Name { get; set; }
    
    ushort Map { get; set; }
    ushort Level { get; set; }
    int Health { get; set; }
    int CurrentHealth { get; set; }
    int Mana { get; set; }
    int CurrentMana { get; set; }
    
    void OnHit(ICharacter attacker, int damage);
    void OnHit(ICreature attacker, int damage);
}

public interface ICharacterInventory
{
    Task LoadAsync(IReadOnlyCollection<object> items);
}

public interface ICharacterSpells
{
    Task LoadAsync(IReadOnlyCollection<object> spells);
}
