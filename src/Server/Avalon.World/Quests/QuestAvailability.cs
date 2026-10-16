using Avalon.Network.Packets.Quest;
using Avalon.World.Entities;

namespace Avalon.World.Quests;

/// <summary>
/// Whether a character may accept a quest now (#433): not done, not held, its prerequisite done, the level and
/// class met, the quest's script (if any) allowing it, and room in the log. Pure. The NPC check (is this the giver)
/// is the caller's. LogFull only for a quest that is otherwise available, so a full log hides nothing.
/// The script check is asked last, with the quest and the character, so the caller can hand one delegate made once
/// rather than a closure per call (#973).
/// </summary>
public static class QuestAvailability
{
    public static QuestResult Check(QuestView quest, CharacterEntity character, int maxActive,
        Func<QuestView, CharacterEntity, bool> scriptAllows)
    {
        QuestLog log = character.Quests;
        if (log.IsCompleted(quest.Id) || log.IsActive(quest.Id))
            return QuestResult.NotAvailable;
        if (quest.RequiredQuestId is { } required && !log.IsCompleted(required))
            return QuestResult.NotAvailable;
        if (character.Level < quest.LevelRequirement)
            return QuestResult.NotAvailable;
        if (quest.ClassRequirement is { } characterClass && character.Class != characterClass)
            return QuestResult.NotAvailable;
        if (!scriptAllows(quest, character))
            return QuestResult.NotAvailable;

        return log.ActiveCount >= maxActive ? QuestResult.LogFull : QuestResult.Ok;
    }
}
