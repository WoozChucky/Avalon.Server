using Avalon.World.Quests;

namespace Avalon.World.Talents;

/// <summary>
/// The talent points a character has earned (#1181): one per level, plus what each quest it has turned in pays
/// (<see cref="QuestView.RewardTalentPoints" />). Derived whenever it is needed, never stored: the points spent are the
/// nodes held (<see cref="TalentAllocation.Spent" />).
/// </summary>
public static class TalentPoints
{
    /// <summary>
    /// <paramref name="level" /> plus the talent points of every quest in <paramref name="quests" />' completions that
    /// <paramref name="catalog" /> holds; a quest it no longer holds pays nothing, and a repeatable one pays nothing
    /// (the catalog refuses one that would). Walks the completions' struct enumerator: allocates nothing.
    /// </summary>
    public static int Earned(ushort level, QuestLog quests, QuestCatalog catalog)
    {
        int earned = level;
        foreach (QuestCompletion completion in quests.Completions)
        {
            if (catalog.TryGet(completion.QuestId, out QuestView? quest))
                earned += checked((int)quest.RewardTalentPoints);
        }

        return earned;
    }
}
