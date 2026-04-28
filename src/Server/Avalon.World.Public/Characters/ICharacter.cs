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

    /// <summary>True while the character is in the dead state — gates input/attack/enter handlers
    /// and pauses regen. Cleared by <see cref="Revive"/>.</summary>
    bool IsDead { get; set; }

    /// <summary>Atomically clears <see cref="IsDead"/> and restores <c>CurrentHealth</c> to
    /// <c>Health</c>. Called by the respawn handler and by the logout-while-dead branch in
    /// <c>World.DeSpawnPlayerAsync</c> so the persisted row lands with full HP at the town.</summary>
    void Revive();

    /// <summary>Records a combat event (hit received or attack sent). Resets the out-of-combat timer.</summary>
    void MarkCombat();

    void OnDisconnected();
    float GetMovementSpeed();

    void Update(TimeSpan deltaTime);
}
