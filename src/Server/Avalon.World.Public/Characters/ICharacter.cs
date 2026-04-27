using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Characters;

public delegate void CharacterDisconnectedDelegate(ICharacter character);

public interface ICharacter : IUnit
{
    ICharacterGameState CharacterGameState { get; }
    ICharacterInventory this[InventoryType type] { get; }

    ICharacterSpells Spells { get; }

    /// <summary>The live instance this character is currently in.</summary>
    Guid InstanceId { get; set; }

    string Name { get; set; }

    MapId Map { get; set; }
    ulong Experience { get; set; }
    ulong RequiredExperience { get; set; }

    /// <summary>Records a combat event (hit received or attack sent). Resets the out-of-combat timer.</summary>
    void MarkCombat();

    void OnDisconnected();
    float GetMovementSpeed();

    void Update(TimeSpan deltaTime);
}
