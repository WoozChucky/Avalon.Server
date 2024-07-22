using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Characters;

public delegate void CharacterDisconnectedDelegate(ICharacter character);

public interface ICharacter : IUnit
{
    IWorldConnection Connection { get; }
    IGameState GameState { get; }
    ICharacterInventory this[InventoryType type] { get; }
    
    ICharacterSpells Spells { get; }
    
    uint ChunkId { get; set; }
    
    string Name { get; set; }
    
    ushort Map { get; set; }
    
    void OnDisconnected();
    float GetMovementSpeed();
    void SetRunning(bool running);
}

public interface ICharacterInventory
{
    void Load(IReadOnlyCollection<object> items);
}
