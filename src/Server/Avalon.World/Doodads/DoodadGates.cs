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
    /// NotStarted: the quest is neither held nor turned in. OnObjective: the quest is held, not yet ready to turn in, its
    /// current stage holds the objective, and the objective's count is below the catalog's. Completed: turned in.
    /// NotCompleted: not turned in. A quest the catalog no longer has, and a condition this build does not know, let no
    /// one through on OnObjective or on the unknown condition.
    /// </summary>
    public static bool Allows(in DoodadGate? gate, QuestLog log, QuestCatalog quests)
    {
        if (gate is not { } rule)
            return true;

        uint questId = rule.QuestId;
        return rule.Condition switch
        {
            DoodadGateCondition.NotStarted => !log.IsActive(questId) && !log.IsCompleted(questId),
            DoodadGateCondition.OnObjective => OnObjective(log, quests, questId, rule.ObjectiveId),
            DoodadGateCondition.Completed => log.IsCompleted(questId),
            DoodadGateCondition.NotCompleted => !log.IsCompleted(questId),
            _ => false,
        };
    }

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
