using Avalon.World.Entities;
using Avalon.World.Public.Scripts;

namespace Avalon.World.Quests;

/// <summary>
/// One character's copy of one quest, as its script's hook is handed it (#433). Advance goes through
/// QuestService.ScriptAdvance, which refuses anything but this quest's Scripted objectives in the current stage,
/// and refuses a context whose quest was abandoned or turned in since.
/// </summary>
internal sealed class QuestContext(QuestService service, CharacterEntity character, QuestView quest, ActiveQuest active) : IQuestContext
{
    public uint QuestId => quest.Id;

    public IQuestCharacter Character { get; } = new QuestCharacterView(character);

    public int Stage => active.Stage;

    public uint ProgressOf(uint objectiveId) => active.ProgressOf(objectiveId);

    public bool Advance(uint objectiveId, uint amount) => service.ScriptAdvance(character, quest, active, objectiveId, amount);
}
