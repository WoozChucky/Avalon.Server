using Avalon.Common.ValueObjects;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;

namespace Avalon.World.Public.Characters;

public delegate void CharacterDisconnectedDelegate(ICharacter character);

public interface ICharacter : IGameEntity
{
    public static event CharacterDisconnectedDelegate? CharacterDisconnected;
    IWorldConnection Connection { get; }
    IGameState GameState { get; }
    ICharacterInventory this[InventoryType type] { get; }
    
    ICharacterSpells Spells { get; }
    
    uint ChunkId { get; set; }
    
    string Name { get; set; }
    
    ushort Map { get; set; }
    
    void OnHit(ICharacter attacker, uint damage);
    void OnHit(ICreature attacker, uint damage);
    void OnDisconnected();
    float GetMovementSpeed();
    void SetRunning(bool running);
}

public interface ICharacterInventory
{
    void Load(IReadOnlyCollection<object> items);
}
