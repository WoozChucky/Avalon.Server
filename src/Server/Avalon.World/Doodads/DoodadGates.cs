using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Quests;

namespace Avalon.World.Doodads;

/// <summary>
/// The quest predicates a doodad's gates ask of one viewer's quest log. Tick thread only, as the log is; allocates
/// nothing. World-side: a gate decides what a player's client is told exists, which no script may decide for it.
/// </summary>
public static class DoodadGates
{
    /// <summary>
    /// Whether <paramref name="gate" /> lets the owner of <paramref name="log" /> through; no gate lets everyone through.
    /// NotStarted: the quest is neither held nor completed. OnObjective: the quest is held, not yet ready to turn in, its
    /// current stage holds the objective, and the objective's count is below the catalog's. Completed: completed.
    /// NotCompleted: not completed. "Completed" follows availability (#710): with <paramref name="resets" />, it is
    /// <see cref="QuestResetClock.CompletedThisPeriod" />, so a repeatable quest that may be done again reads as not
    /// completed (a plain repeatable at once, a daily, weekly or monthly one after its next reset) and its doodads come
    /// back; without one (a visibility built without the clock, as in a test that places no repeatable) it is "turned
    /// in at all". A quest the catalog no longer has, and a condition this build does not know, let no one through on
    /// OnObjective or on the unknown condition.
    /// </summary>
    public static bool Allows(in DoodadGate? gate, QuestLog log, QuestCatalog quests, QuestResetClock? resets)
    {
        if (gate is not { } rule)
            return true;

        uint questId = rule.QuestId;
        return rule.Condition switch
        {
            DoodadGateCondition.NotStarted => !log.IsActive(questId) && !Completed(log, quests, questId, resets),
            DoodadGateCondition.OnObjective => OnObjective(log, quests, questId, rule.ObjectiveId),
            DoodadGateCondition.Completed => Completed(log, quests, questId, resets),
            DoodadGateCondition.NotCompleted => !Completed(log, quests, questId, resets),
            _ => false,
        };
    }

    /// <summary>
    /// What a gate means by completed: in the current period of the quest's repeat kind when there is a reset clock,
    /// else turned in at all. A script's view context asks it too.
    /// </summary>
    internal static bool Completed(QuestLog log, QuestCatalog quests, uint questId, QuestResetClock? resets) =>
        resets?.CompletedThisPeriod(log, quests, questId) ?? log.IsCompleted(questId);

    /// <summary>
    /// The OnObjective rule: the quest held, not ready to turn in, its current stage holding the objective, and the
    /// objective's count below the catalog's. A script's view context asks it too. Walked by index rather than through
    /// QuestView.Objective, whose LINQ allocates on every call.
    /// </summary>
    internal static bool OnObjective(QuestLog log, QuestCatalog quests, uint questId, uint? objectiveId)
    {
        if (objectiveId is not { } objective
            || log.Get(questId) is not { State: CharacterQuestState.Active } held
            || !quests.TryGet(questId, out QuestView? quest)
            || held.Stage < 0 || held.Stage >= quest.Stages.Count)
        {
            return false;
        }

        IReadOnlyList<QuestObjectiveView> objectives = quest.Stages[held.Stage].Objectives;
        for (int i = 0; i < objectives.Count; i++)
        {
            QuestObjectiveView candidate = objectives[i];
            if (candidate.Id == objective)
                return held.ProgressOf(objective) < candidate.Count;
        }

        return false;
    }
}
