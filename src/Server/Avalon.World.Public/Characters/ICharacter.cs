using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Characters;

public delegate void CharacterDisconnectedDelegate(ICharacter character);

public interface ICharacter : IUnit
{
    IWorldConnection Connection { get; }
    ICharacterGameState CharacterGameState { get; }
    ICharacterInventory this[InventoryType type] { get; }

    ICharacterSpells Spells { get; }

    ChunkId ChunkId { get; set; }

    string Name { get; set; }

    MapId Map { get; set; }
    ulong Experience { get; set; }
    ulong RequiredExperience { get; set; }

    void OnDisconnected();
    float GetMovementSpeed();
    void SetRunning(bool running);

    void Update(TimeSpan deltaTime);
}
