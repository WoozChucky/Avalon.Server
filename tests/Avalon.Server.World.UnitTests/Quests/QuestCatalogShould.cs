using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging.Abstractions;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// QuestCatalog (#433) refuses a bad quest with a reason naming it and keeps every other, as LootCatalog and
/// VendorCatalog do. One case per refusal in the spec, plus the three the plan adds.
/// </summary>
public class QuestCatalogShould
{
    private sealed class SampleScript : Avalon.World.Public.Scripts.QuestScript;

    private static QuestCatalog Build(IEnumerable<QuestTemplate> quests, List<ItemTemplate>? items = null,
        Func<string, Type?>? scripts = null, List<DialogueNode>? nodes = null) =>
        new(quests.ToList(), QuestTestData.Creatures(), items ?? Items(), nodes ?? Roots(Giver, Ender, TalkTarget),
            scripts ?? FindScript, NullLoggerFactory.Instance);

    private static void AssertRefused(QuestCatalog catalog, uint id, string reasonFragment)
    {
        Assert.False(catalog.TryGet(id, out _));
        QuestRefusal refusal = Assert.Single(catalog.Refused, r => r.QuestId == id);
        Assert.Contains(reasonFragment, refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_the_chain_whole()
    {
        QuestCatalog catalog = Build(Chain());

        Assert.Empty(catalog.Refused);
        Assert.True(catalog.TryGet(Howl, out QuestView? howl));
        Assert.Equal([0, 1, 2], howl!.Stages.Select(s => s.Sequence));
        Assert.Equal([Hunt, Tusks], catalog.GivenBy(new CreatureTemplateId(Giver)).Select(q => q.Id));
        Assert.Equal([Tusks, Howl], catalog.EndedBy(new CreatureTemplateId(Ender)).Select(q => q.Id));
        QuestDropView drop = Assert.Single(catalog.DropsFrom(new CreatureTemplateId(Boar)));
        Assert.Equal((Tusks, TusksCollect, 100f), (drop.QuestId, drop.ObjectiveId, drop.Chance));
        Assert.True(catalog.IsQuestNpc(new CreatureTemplateId(Ender)));
        Assert.False(catalog.IsQuestNpc(new CreatureTemplateId(Boar)));
    }

    [Fact]
    public void List_a_givers_quests_by_level_then_id()
    {
        QuestCatalog catalog = Build([
            Quest(3, level: 2).WithStage(0, Kill(31, Boar, 1)),
            Quest(2, level: 1).WithStage(0, Kill(21, Boar, 1)),
            Quest(1, level: 2).WithStage(0, Kill(11, Boar, 1)),
        ]);

        Assert.Equal([2u, 1u, 3u], catalog.GivenBy(new CreatureTemplateId(Giver)).Select(q => q.Id));
    }

    public static TheoryData<QuestTemplate, string> BrokenQuests => new()
    {
        { Quest(1, giver: 999).WithStage(0, Kill(11, Boar, 1)), "giver" },
        { Quest(1, ender: 999).WithStage(0, Kill(11, Boar, 1)), "ender" },
        { Quest(1).WithStage(0, Kill(11, 999, 1)), "creature template 999" },
        { Quest(1).WithStage(0, Collect(11, 999, 1)), "item template 999" },
        { Quest(1).WithStage(0, Kill(11, Boar, 1)).Paying(999, 1), "item template 999" },
        { Quest(1, requires: 999).WithStage(0, Kill(11, Boar, 1)), "prerequisite" },
        { Quest(1), "no stages" },
        { Quest(1).WithStage(0, Kill(11, Boar, 0)), "count 0" },
        { Quest(1, script: "Nope").WithStage(0, Kill(11, Boar, 1)), "script" },
        { Quest(0).WithStage(0, Kill(11, Boar, 1)), "id" },
        { Quest(1).WithStage(0, Collect(11, Tonic, 1)), "not a QuestItem" },
        { Quest(1).WithStage(0, Collect(11, Tusk, 1)).WithStage(1, Collect(12, Tusk, 1)), "objective 12 collects item template 7101, which objective 11 already collects" },
    };

    [Theory]
    [MemberData(nameof(BrokenQuests))]
    public void Refuse_a_quest_that_breaks_a_rule(QuestTemplate quest, string reasonFragment) =>
        AssertRefused(Build([quest]), (uint)quest.Id.Value, reasonFragment);

    /// <summary>Final review M3: only the quest's script can move a Scripted objective, so a quest without one would stick.</summary>
    [Fact]
    public void Refuse_a_scripted_objective_on_a_quest_without_a_script()
    {
        QuestCatalog catalog = Build([
            Quest(1).WithStage(0, Scripted(11, 1)),
            Quest(2, script: nameof(SampleQuestScript)).WithStage(0, Scripted(21, 1)),
        ]);

        AssertRefused(catalog, 1, "objective 11 is Scripted but the quest has no script");
        Assert.True(catalog.TryGet(2, out _));
    }

    /// <summary>
    /// #737: an NPC with no dialogue root cannot be talked to (InteractHandler drops the interact), so a quest it gives,
    /// takes back or must be spoken to for could never be accepted, handed in or finished.
    /// </summary>
    [Fact]
    public void Refuse_a_giver_with_no_dialogue_root() =>
        AssertRefused(Build([Quest(1).WithStage(0, Kill(11, Boar, 1))], nodes: Roots(Ender, TalkTarget)), 1,
            $"giver creature template {Giver} has no dialogue root");

    [Fact]
    public void Refuse_an_ender_with_no_dialogue_root() =>
        AssertRefused(Build([Quest(1, ender: Ender).WithStage(0, Kill(11, Boar, 1))], nodes: Roots(Giver, TalkTarget)), 1,
            $"ender creature template {Ender} has no dialogue root");

    [Fact]
    public void Refuse_a_talk_target_with_no_dialogue_root() =>
        AssertRefused(Build([Quest(1).WithStage(0, Talk(11, TalkTarget))], nodes: Roots(Giver, Ender)), 1,
            $"objective 11 talks to creature template {TalkTarget}, which has no dialogue root");

    /// <summary>A node that is not a root does not count: the interact opens only at a root.</summary>
    [Fact]
    public void Count_only_a_root_node_as_a_dialogue_root()
    {
        List<DialogueNode> nodes = Roots(Giver, Ender);
        nodes.Add(new DialogueNode { Id = 9903, CreatureTemplateId = TalkTarget, IsRoot = false, TextId = BodyText });

        AssertRefused(Build([Quest(1).WithStage(0, Talk(11, TalkTarget))], nodes: nodes), 1, "no dialogue root");
    }

    /// <summary>#737: what a /reload dialogue checks against the loaded quests; it refuses nothing.</summary>
    [Fact]
    public void Name_every_loaded_quest_whose_npc_has_no_root_in_a_dialogue_catalog()
    {
        QuestCatalog catalog = Build(Chain());
        var dialogue = new Avalon.World.Dialogue.DialogueCatalog(Roots(Giver), [], NullLoggerFactory.Instance);

        IReadOnlyList<string> problems = catalog.NpcsWithoutDialogue(dialogue);

        // One line per quest, listing all of its problems (fix round 1).
        Assert.Equal(
        [
            $"quest {Tusks}: ender creature template {Ender} has no dialogue root",
            $"quest {Howl}: giver creature template {Ender} has no dialogue root; ender creature template {Ender} has no " +
            $"dialogue root; objective {HowlTalk} talks to creature template {TalkTarget}, which has no dialogue root",
        ], problems);
        Assert.Empty(catalog.NpcsWithoutDialogue(new Avalon.World.Dialogue.DialogueCatalog(Roots(Giver, Ender, TalkTarget), [],
            NullLoggerFactory.Instance)));
    }

    [Fact]
    public void Refuse_a_prerequisite_cycle()
    {
        QuestCatalog catalog = Build([
            Quest(1, requires: 2).WithStage(0, Kill(11, Boar, 1)),
            Quest(2, requires: 1).WithStage(0, Kill(21, Boar, 1)),
            Quest(3).WithStage(0, Kill(31, Boar, 1)),
        ]);

        AssertRefused(catalog, 1, "cycle");
        AssertRefused(catalog, 2, "cycle");
        Assert.True(catalog.TryGet(3, out _));
    }

    [Fact]
    public void Refuse_a_quest_whose_prerequisite_was_refused()
    {
        QuestCatalog catalog = Build([Quest(1, giver: 999).WithStage(0, Kill(11, Boar, 1)), Quest(2, requires: 1).WithStage(0, Kill(21, Boar, 1))]);

        AssertRefused(catalog, 2, "prerequisite");
    }

    [Fact]
    public void Refuse_stages_that_are_not_contiguous_from_zero()
    {
        AssertRefused(Build([Quest(1).WithStage(1, Kill(11, Boar, 1))]), 1, "stages");
        AssertRefused(Build([Quest(2).WithStage(0, Kill(21, Boar, 1)).WithStage(2, Kill(22, Boar, 1))]), 2, "stages");
    }

    [Fact]
    public void Refuse_a_stage_without_objectives_or_with_more_than_four()
    {
        QuestTemplate empty = Quest(1).WithStage(0, Kill(11, Boar, 1));
        empty.Stages.Add(new QuestStage { QuestId = 1, Sequence = 1 });
        AssertRefused(Build([empty]), 1, "stage 1 has no objectives");

        AssertRefused(Build([Quest(2).WithStage(0, Kill(21, Boar, 1), Kill(22, Boar, 1), Kill(23, Boar, 1), Kill(24, Boar, 1), Kill(25, Boar, 1))]),
            2, "more than 4");
    }

    [Theory]
    [InlineData(-0.5f)]
    [InlineData(100.5f)]
    [InlineData(float.NaN)]
    public void Refuse_a_drop_chance_outside_zero_to_one_hundred(float chance) =>
        AssertRefused(Build([Quest(1).WithStage(0, Collect(11, Tusk, 1, (Boar, chance)))]), 1, "chance");

    [Theory]
    [InlineData(QuestObjectiveType.Kill)]
    [InlineData(QuestObjectiveType.Talk)]
    [InlineData(QuestObjectiveType.Scripted)]
    public void Refuse_a_drop_on_an_objective_that_does_not_collect(QuestObjectiveType type)
    {
        // No database check can say this (the drop names its objective, not the objective's type), so the
        // catalog does: a drop on anything but a Collect objective has no item to drop.
        QuestObjective objective = type switch
        {
            QuestObjectiveType.Kill => Kill(11, Boar, 1),
            QuestObjectiveType.Talk => Talk(11, TalkTarget),
            _ => Scripted(11, 1),
        };
        objective.Drops.Add(new QuestItemDrop { ObjectiveId = 11, CreatureTemplateId = Boar, Chance = 50f });

        // A Scripted objective needs its quest to have a script, or that is refused first.
        string? script = type == QuestObjectiveType.Scripted ? nameof(SampleQuestScript) : null;
        QuestCatalog catalog = Build([Quest(1, script: script).WithStage(0, objective), Quest(2).WithStage(0, Kill(21, Boar, 1))]);

        AssertRefused(catalog, 1, "objective 11 has drops but does not collect");
        Assert.Empty(catalog.DropsFrom(new CreatureTemplateId(Boar)));
        Assert.True(catalog.TryGet(2, out _));
    }

    public static TheoryData<QuestObjectiveType, ulong?, ulong?, string> MisfitTargets => new()
    {
        { QuestObjectiveType.Kill, null, null, "is Kill and must name a creature template" },
        { QuestObjectiveType.Kill, Boar, Tusk, "is Kill and must name a creature template and no item" },
        { QuestObjectiveType.Talk, null, null, "is Talk and must name a creature template" },
        { QuestObjectiveType.Talk, TalkTarget, Tusk, "is Talk and must name a creature template and no item" },
        { QuestObjectiveType.Collect, null, null, "is Collect and must name an item template" },
        { QuestObjectiveType.Collect, Boar, Tusk, "is Collect and must name an item template and no creature" },
        { QuestObjectiveType.Scripted, Boar, null, "is Scripted and must name neither" },
        { QuestObjectiveType.Scripted, null, Tusk, "is Scripted and must name neither" },
        { (QuestObjectiveType)99, null, null, "unknown type 99" },
    };

    /// <summary>
    /// A database check pins each type's target, but the catalog checks again, so a row that slipped past (a Collect
    /// with no item used to throw on its null and fail the whole area, at startup too) refuses only its quest.
    /// </summary>
    [Theory]
    [MemberData(nameof(MisfitTargets))]
    public void Refuse_an_objective_whose_target_does_not_fit_its_type(QuestObjectiveType type, ulong? creature,
        ulong? item, string reasonFragment)
    {
        var objective = new QuestObjective
        {
            Id = 11,
            Type = type,
            Count = 1,
            DescriptionTextId = ObjectiveText,
            CreatureTemplateId = creature is { } c ? new CreatureTemplateId(c) : null,
            ItemTemplateId = item is { } i ? new ItemTemplateId(i) : null,
        };

        QuestCatalog catalog = Build([Quest(1).WithStage(0, objective), Quest(2).WithStage(0, Kill(21, Boar, 1))]);

        AssertRefused(catalog, 1, reasonFragment);
        Assert.StartsWith("objective 11 ", Assert.Single(catalog.Refused).Reason, StringComparison.Ordinal);
        Assert.True(catalog.TryGet(2, out _));
    }

    [Fact]
    public void Let_a_quest_collect_an_item_whose_earlier_collector_was_refused()
    {
        // Quest 1 is refused for its prerequisite, so it holds no item: quest 2 may collect the tusk, and no
        // refusal names a quest that is not loaded.
        QuestCatalog catalog = Build([
            Quest(1, requires: 999).WithStage(0, Collect(11, Tusk, 1)),
            Quest(2).WithStage(0, Collect(21, Tusk, 1, (Boar, 50f))),
        ]);

        AssertRefused(catalog, 1, "prerequisite");
        Assert.True(catalog.TryGet(2, out _));
        Assert.Equal(2u, Assert.Single(catalog.DropsFrom(new CreatureTemplateId(Boar))).QuestId);
    }

    [Fact]
    public void Refuse_the_dependants_of_a_quest_refused_for_a_collected_item()
    {
        QuestCatalog catalog = Build([
            Quest(1).WithStage(0, Collect(11, Tusk, 1)),
            Quest(2).WithStage(0, Collect(21, Tusk, 1)),
            Quest(3, requires: 2).WithStage(0, Kill(31, Boar, 1)),
        ]);

        Assert.True(catalog.TryGet(1, out _));
        AssertRefused(catalog, 2, "already collected by quest 1");
        AssertRefused(catalog, 3, "prerequisite quest 2");
    }

    [Fact]
    public void Refuse_a_unique_reward_item()
    {
        List<ItemTemplate> items = Items();
        items.Single(i => i.Id.Value == Charm).Flags = ItemTemplateFlags.Unique;

        AssertRefused(Build([Quest(1).WithStage(0, Kill(11, Boar, 1)).Paying(Charm, 1)], items), 1, "Unique");
    }

    [Fact]
    public void Resolve_a_loaded_script_to_its_type()
    {
        QuestCatalog catalog = Build([Quest(1, script: "SampleScript").WithStage(0, Kill(11, Boar, 1))],
            scripts: name => name == "SampleScript" ? typeof(SampleScript) : null);

        Assert.True(catalog.TryGet(1, out QuestView? quest));
        Assert.Equal(typeof(SampleScript), quest!.ScriptType);
    }
}
