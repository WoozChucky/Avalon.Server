using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// The quest fixtures every quest test shares: three NPCs (a giver, an ender, a talk target), two creatures (a
/// boar that drops the tusk, a wolf), three items (the tusk quest item, a stackable reward, a one-off reward) and
/// three chained quests. Numbers are far from the seed's so a failure names the fixture, not the storyline.
/// </summary>
internal static class QuestTestData
{
    public const ulong Giver = 701, Ender = 702, TalkTarget = 703, Boar = 704, Wolf = 705;
    public const ulong Tusk = 7101, Tonic = 7102, Charm = 7103;
    public const uint Hunt = 7201, Tusks = 7202, Howl = 7203;
    public const uint HuntKill = 72011, TusksCollect = 72021, HowlKill = 72031, HowlTalk = 72032, HowlScripted = 72033;
    public const int TitleText = 7300, ObjectiveText = 7301, StageText = 7302, BodyText = 7303, DoneText = 7304;

    public static CreatureTemplate Creature(ulong id, string name) => new() { Id = id, Name = name };

    public static List<CreatureTemplate> Creatures() =>
    [
        Creature(Giver, "Giver"), Creature(Ender, "Ender"), Creature(TalkTarget, "Marta"),
        Creature(Boar, "Boar"), Creature(Wolf, "Wolf"),
    ];

    public static ItemTemplate TuskItem() => new()
    {
        Id = Tusk, Name = "Tusk", Class = ItemClass.Quest, SubClass = ItemSubClass.QuestItem, MaxStackSize = 20,
        Flags = ItemTemplateFlags.QuestItem | ItemTemplateFlags.NoSell,
    };

    public static List<ItemTemplate> Items() =>
    [
        TuskItem(),
        new() { Id = Tonic, Name = "Tonic", Class = ItemClass.Consumable, MaxStackSize = 10 },
        new() { Id = Charm, Name = "Charm", Class = ItemClass.Armor, MaxStackSize = 1 },
    ];

    public static List<LocalizedText> Texts() =>
    [
        new() { Id = TitleText, Text = "A Test Quest" },
        new() { Id = ObjectiveText, Text = "Things done" },
        new() { Id = StageText, Text = "Do the thing" },
        new() { Id = BodyText, Text = "Please do the thing, {name}." },
        new() { Id = DoneText, Text = "Well done." },
    ];

    public static QuestTemplate Quest(uint id, ulong giver = Giver, ulong ender = Giver, ushort level = 1,
        uint? requires = null, string? script = null) => new()
    {
        Id = id,
        TitleTextId = TitleText,
        DescriptionTextId = BodyText,
        CompletionTextId = DoneText,
        GiverCreatureId = giver,
        EnderCreatureId = ender,
        LevelRequirement = level,
        RequiredQuestId = requires is { } r ? new QuestTemplateId(r) : null,
        RewardExperience = 50,
        RewardMoney = 30,
        ScriptName = script,
    };

    public static QuestTemplate WithStage(this QuestTemplate quest, int sequence, params QuestObjective[] objectives)
    {
        quest.Stages.Add(new QuestStage { QuestId = quest.Id, Sequence = sequence, DescriptionTextId = StageText });
        foreach (QuestObjective objective in objectives)
        {
            objective.QuestId = quest.Id;
            objective.StageSequence = sequence;
            quest.Objectives.Add(objective);
        }

        return quest;
    }

    public static QuestTemplate Paying(this QuestTemplate quest, ulong item, uint count)
    {
        quest.ItemRewards.Add(new QuestItemReward { QuestId = quest.Id, ItemTemplateId = item, Count = count });
        return quest;
    }

    public static QuestObjective Kill(uint id, ulong creature, uint count) => new()
        { Id = id, Type = QuestObjectiveType.Kill, CreatureTemplateId = creature, Count = count, DescriptionTextId = ObjectiveText };

    public static QuestObjective Talk(uint id, ulong creature) => new()
        { Id = id, Type = QuestObjectiveType.Talk, CreatureTemplateId = creature, Count = 1, DescriptionTextId = ObjectiveText };

    public static QuestObjective Scripted(uint id, uint count) => new()
        { Id = id, Type = QuestObjectiveType.Scripted, Count = count, DescriptionTextId = ObjectiveText };

    public static QuestObjective Collect(uint id, ulong item, uint count, params (ulong Creature, float Chance)[] drops)
    {
        var objective = new QuestObjective
            { Id = id, Type = QuestObjectiveType.Collect, ItemTemplateId = item, Count = count, DescriptionTextId = ObjectiveText };
        foreach ((ulong creature, float chance) in drops)
            objective.Drops.Add(new QuestItemDrop { ObjectiveId = id, CreatureTemplateId = creature, Chance = chance });
        return objective;
    }

    /// <summary>
    /// Hunt (kill 2 boars at the giver), then Tusks (collect 2 tusks from boars at 100 %, hand in to the ender,
    /// pays 2 tonics), then Howl (kill a wolf; talk to Marta; one scripted step — given by and handed to the ender).
    /// </summary>
    public static List<QuestTemplate> Chain() =>
    [
        Quest(Hunt).WithStage(0, Kill(HuntKill, Boar, 2)),
        Quest(Tusks, ender: Ender, requires: Hunt).WithStage(0, Collect(TusksCollect, Tusk, 2, (Boar, 100f))).Paying(Tonic, 2),
        Quest(Howl, giver: Ender, ender: Ender, level: 2, requires: Tusks)
            .WithStage(0, Kill(HowlKill, Wolf, 1))
            .WithStage(1, Talk(HowlTalk, TalkTarget))
            .WithStage(2, Scripted(HowlScripted, 1))
            .Paying(Charm, 1),
    ];
}

internal static class QuestRepositories
{
    public static Avalon.Database.World.Repositories.IQuestRepository Of(Func<IReadOnlyCollection<QuestTemplate>> quests)
    {
        var repository = NSubstitute.Substitute.For<Avalon.Database.World.Repositories.IQuestRepository>();
        NSubstitute.SubstituteExtensions.Returns(repository.GetAllAsync(NSubstitute.Arg.Any<CancellationToken>()),
            _ => Task.FromResult(quests()));
        return repository;
    }
}
