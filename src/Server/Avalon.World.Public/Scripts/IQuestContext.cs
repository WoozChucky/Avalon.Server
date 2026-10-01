using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Scripts;

/// <summary>A character as a quest script may see it: read-only, so no hook can grant anything (#433).</summary>
public interface IQuestCharacter
{
    uint CharacterId { get; }
    string Name { get; }
    ushort Level { get; }
    CharacterClass Class { get; }
}

/// <summary>
/// What a quest script's hook gets for one character's copy of its quest (#433). It reads the character and the
/// quest's progress, and its one write is <see cref="Advance" /> on the quest's own Scripted objectives in the
/// current stage. There is no grant, inventory, teleport or other-quest surface.
/// </summary>
public interface IQuestContext
{
    uint QuestId { get; }

    IQuestCharacter Character { get; }

    /// <summary>The current stage's sequence, from 0.</summary>
    int Stage { get; }

    /// <summary>The objective's count so far; 0 for an objective the quest has not reached or does not have.</summary>
    uint ProgressOf(uint objectiveId);

    /// <summary>
    /// Adds <paramref name="amount" /> to one of this quest's Scripted objectives in the current stage, capped at its
    /// count. False, changing nothing and logging a warning, for any other objective, a finished one, or 0.
    /// </summary>
    bool Advance(uint objectiveId, uint amount);
}
