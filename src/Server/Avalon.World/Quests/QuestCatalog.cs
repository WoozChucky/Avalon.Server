using System.Diagnostics.CodeAnalysis;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Quests;

public sealed record QuestObjectiveView(
    uint Id, uint QuestId, int StageSequence, QuestObjectiveType Type,
    CreatureTemplateId? CreatureTemplateId, ItemTemplateId? ItemTemplateId, uint Count, LocalizedTextId DescriptionTextId);

public sealed record QuestStageView(int Sequence, LocalizedTextId? DescriptionTextId, IReadOnlyList<QuestObjectiveView> Objectives);

public sealed record QuestItemRewardView(ItemTemplateId ItemTemplateId, uint Count);

/// <summary>A Collect objective's item drops from a creature at this chance, a percentage.</summary>
public sealed record QuestDropView(uint QuestId, uint ObjectiveId, ItemTemplateId ItemTemplateId, float Chance);

public sealed record QuestView(
    uint Id,
    LocalizedTextId TitleTextId,
    LocalizedTextId DescriptionTextId,
    LocalizedTextId CompletionTextId,
    CreatureTemplateId GiverCreatureId,
    CreatureTemplateId EnderCreatureId,
    ushort LevelRequirement,
    CharacterClass? ClassRequirement,
    uint? RequiredQuestId,
    uint RewardExperience,
    ulong RewardMoney,
    Type? ScriptType,
    IReadOnlyList<QuestStageView> Stages,
    IReadOnlyList<QuestItemRewardView> ItemRewards)
{
    public QuestStageView LastStage => Stages[^1];

    public IEnumerable<QuestObjectiveView> Objectives => Stages.SelectMany(s => s.Objectives);

    public QuestObjectiveView? Objective(uint objectiveId) => Objectives.FirstOrDefault(o => o.Id == objectiveId);
}

/// <summary>A quest left out of the catalog, and why.</summary>
public sealed record QuestRefusal(uint QuestId, string Reason)
{
    public override string ToString() => $"quest {QuestId}: {Reason}";
}

/// <summary>
/// Every quest that passed validation (#433), built off the tick by the Quests reload area and immutable
/// afterwards, so the tick reads it without locking. A bad quest is refused with an error naming it and every other
/// quest still loads; a quest whose prerequisite is refused is refused too.
/// </summary>
public sealed class QuestCatalog
{
    public const int MaxObjectivesPerStage = 4;

    public static readonly QuestCatalog Empty = new([], [], [], _ => null, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

    private static readonly IReadOnlyList<QuestView> NoQuests = [];
    private static readonly IReadOnlyList<QuestDropView> NoDrops = [];

    private readonly Dictionary<uint, QuestView> _byId;
    private readonly Dictionary<ulong, IReadOnlyList<QuestView>> _byGiver;
    private readonly Dictionary<ulong, IReadOnlyList<QuestView>> _byEnder;
    private readonly Dictionary<ulong, IReadOnlyList<QuestDropView>> _dropsByCreature;

    public QuestCatalog(
        IReadOnlyCollection<QuestTemplate> quests,
        IReadOnlyCollection<CreatureTemplate> creatures,
        IReadOnlyCollection<ItemTemplate> items,
        Func<string, Type?> findScript,
        ILoggerFactory loggerFactory)
    {
        ILogger<QuestCatalog> logger = loggerFactory.CreateLogger<QuestCatalog>();
        HashSet<ulong> knownCreatures = creatures.Select(c => c.Id.Value).ToHashSet();
        Dictionary<ulong, ItemTemplate> knownItems = [];
        foreach (ItemTemplate item in items)
            knownItems.TryAdd(item.Id.Value, item);

        List<QuestRefusal> refused = [];
        Dictionary<uint, (QuestTemplate Row, Type? Script)> candidates = [];

        foreach (QuestTemplate quest in quests.OrderBy(q => q.Id.Value))
        {
            string? reason = Problem(quest, knownCreatures, knownItems, findScript, out Type? script);
            if (reason is not null)
            {
                refused.Add(new QuestRefusal(quest.Id.Value, reason));
                continue;
            }

            candidates[quest.Id.Value] = (quest, script);
        }

        RefuseBrokenPrerequisites(candidates, refused);

        // Only now, over the quests that survived, so a quest refused for its own reasons never blocks another's
        // item with a reason naming a quest that is not loaded. A quest refused here may be another's
        // prerequisite, so the cascade runs again.
        if (RefuseSecondCollectors(candidates, refused))
            RefuseBrokenPrerequisites(candidates, refused);

        _byId = candidates.ToDictionary(c => c.Key, c => ToView(c.Value.Row, c.Value.Script));
        _byGiver = _byId.Values.GroupBy(q => q.GiverCreatureId.Value).ToDictionary(g => g.Key,
            g => (IReadOnlyList<QuestView>)g.OrderBy(q => q.LevelRequirement).ThenBy(q => q.Id).ToList());
        _byEnder = _byId.Values.GroupBy(q => q.EnderCreatureId.Value).ToDictionary(g => g.Key,
            g => (IReadOnlyList<QuestView>)g.OrderBy(q => q.Id).ToList());
        _dropsByCreature = candidates.Values
            .SelectMany(c => c.Row.Objectives.SelectMany(o => o.Drops.Select(d => (Drop: d, Objective: o))))
            .GroupBy(x => x.Drop.CreatureTemplateId.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QuestDropView>)g
                .OrderBy(x => x.Objective.QuestId.Value).ThenBy(x => x.Objective.Id)
                .Select(x => new QuestDropView(x.Objective.QuestId.Value, x.Objective.Id, x.Objective.ItemTemplateId!, x.Drop.Chance))
                .ToList());

        Refused = refused.OrderBy(r => r.QuestId).ToList();
        foreach (QuestRefusal refusal in Refused)
            logger.LogError("Refused {Refusal}. Every other quest still loads", refusal.ToString());

        logger.LogInformation("Loaded {Count} quests; refused {Refused}", _byId.Count, Refused.Count);
    }

    public IReadOnlyCollection<QuestView> All => _byId.Values;

    public IReadOnlyList<QuestRefusal> Refused { get; }

    public bool TryGet(uint questId, [NotNullWhen(true)] out QuestView? quest) => _byId.TryGetValue(questId, out quest);

    /// <summary>The quests this NPC gives, by level requirement then id. No allocation.</summary>
    public IReadOnlyList<QuestView> GivenBy(CreatureTemplateId npc) =>
        _byGiver.TryGetValue(npc.Value, out IReadOnlyList<QuestView>? list) ? list : NoQuests;

    /// <summary>The quests handed in to this NPC, by id. No allocation.</summary>
    public IReadOnlyList<QuestView> EndedBy(CreatureTemplateId npc) =>
        _byEnder.TryGetValue(npc.Value, out IReadOnlyList<QuestView>? list) ? list : NoQuests;

    public IReadOnlyList<QuestDropView> DropsFrom(CreatureTemplateId creature) =>
        _dropsByCreature.TryGetValue(creature.Value, out IReadOnlyList<QuestDropView>? list) ? list : NoDrops;

    public bool IsQuestNpc(CreatureTemplateId npc) => _byGiver.ContainsKey(npc.Value) || _byEnder.ContainsKey(npc.Value);

    public string Describe() => Refused.Count == 0
        ? $"{_byId.Count} quests"
        : $"{_byId.Count} quests, {Refused.Count} refused ({string.Join("; ", Refused)})";

    private static string? Problem(QuestTemplate quest, HashSet<ulong> creatures, Dictionary<ulong, ItemTemplate> items,
        Func<string, Type?> findScript, out Type? script)
    {
        script = null;
        uint id = quest.Id.Value;
        if (id == 0 || id > int.MaxValue)
            return $"id {id} is outside 1-{int.MaxValue}; quest dialogue options carry it as a negative int";
        if (!creatures.Contains(quest.GiverCreatureId.Value))
            return $"giver creature template {quest.GiverCreatureId.Value} does not exist";
        if (!creatures.Contains(quest.EnderCreatureId.Value))
            return $"ender creature template {quest.EnderCreatureId.Value} does not exist";
        if (quest.RequiredQuestId?.Value == id)
            return "its prerequisite is itself (a cycle)";

        if (quest.Stages.Count == 0)
            return "it has no stages";
        List<int> sequences = quest.Stages.Select(s => s.Sequence).Order().ToList();
        if (!sequences.SequenceEqual(Enumerable.Range(0, sequences.Count)))
            return $"stages {string.Join(",", sequences)} do not run 0..n without gaps";

        foreach (QuestStage stage in quest.Stages)
        {
            int count = quest.Objectives.Count(o => o.StageSequence == stage.Sequence);
            if (count == 0)
                return $"stage {stage.Sequence} has no objectives";
            if (count > MaxObjectivesPerStage)
                return $"stage {stage.Sequence} has more than {MaxObjectivesPerStage} objectives";
        }

        Dictionary<ulong, uint> collectedBy = [];
        foreach (QuestObjective objective in quest.Objectives.OrderBy(o => o.Id))
        {
            if (!sequences.Contains(objective.StageSequence))
                return $"objective {objective.Id} names stage {objective.StageSequence}, which it does not have";
            if (objective.Count == 0)
                return $"objective {objective.Id} has count 0";
            if (TargetProblem(objective) is { } target)
                return target;
            if (objective.Type == QuestObjectiveType.Collect
                && !collectedBy.TryAdd(objective.ItemTemplateId!.Value, objective.Id))
                return $"objective {objective.Id} collects item template {objective.ItemTemplateId.Value}, which objective " +
                       $"{collectedBy[objective.ItemTemplateId.Value]} already collects";
            if (objective.CreatureTemplateId is { } creature && !creatures.Contains(creature.Value))
                return $"objective {objective.Id} names creature template {creature.Value}, which does not exist";
            if (objective.ItemTemplateId is { } itemId)
            {
                if (!items.TryGetValue(itemId.Value, out ItemTemplate? item))
                    return $"objective {objective.Id} names item template {itemId.Value}, which does not exist";
                if (objective.Type == QuestObjectiveType.Collect && !item.Flags.HasFlag(ItemTemplateFlags.QuestItem))
                    return $"objective {objective.Id} collects item template {itemId.Value}, which is not a QuestItem";
            }

            foreach (QuestItemDrop drop in objective.Drops)
            {
                // No database check can say this: the drop names its objective, not the objective's type.
                if (objective.Type != QuestObjectiveType.Collect)
                    return $"objective {objective.Id} has drops but does not collect";
                if (!creatures.Contains(drop.CreatureTemplateId.Value))
                    return $"objective {objective.Id} drops from creature template {drop.CreatureTemplateId.Value}, which does not exist";
                if (!(drop.Chance >= 0f && drop.Chance <= 100f))
                    return $"objective {objective.Id} drop chance {drop.Chance} is outside 0-100";
            }
        }

        foreach (QuestItemReward reward in quest.ItemRewards)
        {
            if (!items.TryGetValue(reward.ItemTemplateId.Value, out ItemTemplate? item))
                return $"reward item template {reward.ItemTemplateId.Value} does not exist";
            if (reward.Count == 0)
                return $"reward item template {reward.ItemTemplateId.Value} has count 0";
            if (item.Flags.HasFlag(ItemTemplateFlags.Unique))
                return $"reward item template {reward.ItemTemplateId.Value} is Unique; a turn-in could not always pay it";
        }

        if (quest.ScriptName is { Length: > 0 } name)
        {
            script = findScript(name);
            if (script is null)
                return $"script {name} is not a loaded QuestScript";
        }

        return null;
    }

    /// <summary>
    /// The objective's target must fit its type: Kill and Talk name a creature and no item, Collect an item and no
    /// creature, Scripted neither. A database check says the same; the catalog says it again, as LootCatalog does,
    /// so a row that slipped past is refused by name rather than failing the whole area.
    /// </summary>
    private static string? TargetProblem(QuestObjective objective)
    {
        bool creature = objective.CreatureTemplateId is not null;
        bool item = objective.ItemTemplateId is not null;
        return objective.Type switch
        {
            QuestObjectiveType.Kill or QuestObjectiveType.Talk when !creature || item =>
                $"objective {objective.Id} is {objective.Type} and must name a creature template and no item template",
            QuestObjectiveType.Collect when !item || creature =>
                $"objective {objective.Id} is Collect and must name an item template and no creature template",
            QuestObjectiveType.Scripted when creature || item =>
                $"objective {objective.Id} is Scripted and must name neither a creature template nor an item template",
            QuestObjectiveType.Kill or QuestObjectiveType.Talk or QuestObjectiveType.Collect or QuestObjectiveType.Scripted => null,
            _ => $"objective {objective.Id} has unknown type {(int)objective.Type}",
        };
    }

    /// <summary>
    /// One quest per collected item, so a drop or a pickup credits exactly one quest: over the surviving quests in id
    /// order, the first to collect an item keeps it and every later one is refused. True when it refused any.
    /// </summary>
    private static bool RefuseSecondCollectors(Dictionary<uint, (QuestTemplate Row, Type? Script)> candidates, List<QuestRefusal> refused)
    {
        Dictionary<ulong, uint> collectedBy = [];
        bool any = false;
        foreach ((uint id, (QuestTemplate row, _)) in candidates.OrderBy(c => c.Key).ToList())
        {
            List<ulong> collected = row.Objectives
                .Where(o => o.Type == QuestObjectiveType.Collect)
                .Select(o => o.ItemTemplateId!.Value)
                .ToList();
            ulong? taken = collected.Where(collectedBy.ContainsKey).Select(i => (ulong?)i).FirstOrDefault();
            if (taken is { } item)
            {
                refused.Add(new QuestRefusal(id, $"item template {item} is already collected by quest {collectedBy[item]}"));
                candidates.Remove(id);
                any = true;
                continue;
            }

            foreach (ulong i in collected)
                collectedBy[i] = id;
        }

        return any;
    }

    /// <summary>Refuses, to a fixed point, every quest whose prerequisite is missing, refused, or part of a cycle.</summary>
    private static void RefuseBrokenPrerequisites(Dictionary<uint, (QuestTemplate Row, Type? Script)> candidates, List<QuestRefusal> refused)
    {
        // First every quest that lies on a cycle (walking its chain comes back to itself), all at once, so
        // the order they are met in does not decide which of them is named.
        HashSet<uint> inCycle = [];
        foreach (uint id in candidates.Keys)
        {
            HashSet<uint> seen = [];
            uint? next = candidates[id].Row.RequiredQuestId?.Value;
            while (next is { } step && candidates.TryGetValue(step, out var parent) && seen.Add(step))
            {
                if (step == id)
                {
                    inCycle.Add(id);
                    break;
                }

                next = parent.Row.RequiredQuestId?.Value;
            }
        }

        foreach (uint id in inCycle.Order())
        {
            refused.Add(new QuestRefusal(id, "its prerequisites form a cycle"));
            candidates.Remove(id);
        }

        // Then, to a fixed point, every quest whose prerequisite is missing or was refused.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach ((uint id, (QuestTemplate row, _)) in candidates.OrderBy(c => c.Key).ToList())
            {
                if (row.RequiredQuestId is { } required && !candidates.ContainsKey(required.Value))
                {
                    string why = inCycle.Contains(required.Value) ? "is in a prerequisite cycle" : "is missing or refused";
                    refused.Add(new QuestRefusal(id, $"its prerequisite quest {required.Value} {why}"));
                    candidates.Remove(id);
                    changed = true;
                }
            }
        }
    }

    private static QuestView ToView(QuestTemplate q, Type? script) => new(
        q.Id.Value, q.TitleTextId, q.DescriptionTextId, q.CompletionTextId, q.GiverCreatureId, q.EnderCreatureId,
        q.LevelRequirement, q.ClassRequirement, q.RequiredQuestId?.Value, q.RewardExperience, q.RewardMoney, script,
        q.Stages.OrderBy(s => s.Sequence).Select(s => new QuestStageView(s.Sequence, s.DescriptionTextId,
            q.Objectives.Where(o => o.StageSequence == s.Sequence).OrderBy(o => o.Id)
                .Select(o => new QuestObjectiveView(o.Id, q.Id.Value, o.StageSequence, o.Type, o.CreatureTemplateId,
                    o.ItemTemplateId, o.Count, o.DescriptionTextId))
                .ToList())).ToList(),
        q.ItemRewards.OrderBy(r => r.ItemTemplateId.Value).Select(r => new QuestItemRewardView(r.ItemTemplateId, r.Count)).ToList());
}
