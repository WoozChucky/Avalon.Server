using Avalon.World.Public.Creatures;

namespace Avalon.World.Public.Characters;

public interface ICharacter : IGameEntity<ulong>
{
    ICharacterInventory Inventory { get; }
    
    uint ChunkId { get; set; }
    
    string Name { get; set; }
    
    ushort Map { get; set; }
    ushort Level { get; set; }
    
    void OnHit(ICharacter attacker, int damage);
    void OnHit(ICreature attacker, int damage);
}

public interface ICharacterInventory
{
    Task LoadAsync();
}
