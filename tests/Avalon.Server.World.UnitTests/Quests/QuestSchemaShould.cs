using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// The quest tables (#433) over a real SQLite database built from the model: a quest with its stages,
/// objectives (a use-doodad one among them), rewards and drops round-trips, and the check constraints refuse an objective whose
/// target does not fit its type, a zero count, and a drop chance outside 0-100.
/// </summary>
public sealed class QuestSchemaShould : IDisposable
{
    private readonly SqliteDatabase<WorldDbContext> _database = SqliteDatabase.World();

    public void Dispose() => _database.Dispose();

    // Seeded rows the quest points at: creature templates 1 (Uriel) and 4 (Thornback Boar), item 1, doodad template 1 (the
    // forest chest).
    private static QuestTemplate Quest(uint id) => new()
    {
        Id = id,
        TitleTextId = 1,
        DescriptionTextId = 1,
        CompletionTextId = 1,
        GiverCreatureId = 1,
        EnderCreatureId = 1,
        LevelRequirement = 1,
        RewardExperience = 10,
        RewardMoney = 5,
        Stages = [new QuestStage { QuestId = id, Sequence = 0 }],
    };

    private void Save(params object[] rows)
    {
        using WorldDbContext context = _database.CreateDbContext();
        context.AddRange(rows);
        context.SaveChanges();
    }

    [Fact]
    public void Round_trip_a_quest_with_its_stages_objectives_rewards_and_drops()
    {
        QuestTemplate quest = Quest(9001);
        quest.Objectives =
        [
            new QuestObjective
            {
                Id = 90011, QuestId = 9001, StageSequence = 0, Type = QuestObjectiveType.Collect,
                ItemTemplateId = 1, Count = 3, DescriptionTextId = 1,
                Drops = [new QuestItemDrop { ObjectiveId = 90011, CreatureTemplateId = 4, Chance = 60f }],
            },
            new QuestObjective
            {
                Id = 90012, QuestId = 9001, StageSequence = 0, Type = QuestObjectiveType.UseDoodad,
                DoodadTemplateId = 1, Count = 1, DescriptionTextId = 1,
            },
        ];
        quest.ItemRewards = [new QuestItemReward { QuestId = 9001, ItemTemplateId = 1, Count = 2 }];
        Save(quest);

        using WorldDbContext read = _database.CreateDbContext();
        QuestTemplate loaded = read.QuestTemplates.AsNoTracking()
            .Include(q => q.Stages).Include(q => q.Objectives).ThenInclude(o => o.Drops).Include(q => q.ItemRewards)
            .Single(q => q.Id == new QuestTemplateId(9001));

        Assert.Single(loaded.Stages);
        Assert.Equal(2, loaded.Objectives.Count);
        QuestObjective objective = Assert.Single(loaded.Objectives, o => o.Id == 90011);
        Assert.Equal(60f, Assert.Single(objective.Drops).Chance);
        Assert.Equal(new DoodadTemplateId(1), Assert.Single(loaded.Objectives, o => o.Id == 90012).DoodadTemplateId);
        Assert.Equal(2u, Assert.Single(loaded.ItemRewards).Count);
    }

    [Theory]
    [InlineData(QuestObjectiveType.Kill, null, null, null)]      // Kill needs a creature
    [InlineData(QuestObjectiveType.Kill, 4ul, 1ul, null)]        // and no item
    [InlineData(QuestObjectiveType.Kill, 4ul, null, 1u)]         // and no doodad
    [InlineData(QuestObjectiveType.Collect, 4ul, null, null)]    // Collect needs an item and no creature
    [InlineData(QuestObjectiveType.Talk, null, null, null)]      // Talk needs a creature
    [InlineData(QuestObjectiveType.Scripted, 4ul, null, null)]   // Scripted names nothing
    [InlineData(QuestObjectiveType.Scripted, null, null, 1u)]    // not even a doodad
    [InlineData(QuestObjectiveType.UseDoodad, null, null, null)] // UseDoodad needs a doodad
    [InlineData(QuestObjectiveType.UseDoodad, 4ul, null, 1u)]    // and no creature
    public void Refuse_an_objective_whose_target_does_not_fit_its_type(QuestObjectiveType type, ulong? creature, ulong? item,
        uint? doodad)
    {
        QuestTemplate quest = Quest(9002);
        quest.Objectives =
        [
            new QuestObjective
            {
                Id = 90021, QuestId = 9002, StageSequence = 0, Type = type, Count = 1, DescriptionTextId = 1,
                CreatureTemplateId = creature is { } c ? new CreatureTemplateId(c) : null,
                ItemTemplateId = item is { } i ? new ItemTemplateId(i) : null,
                DoodadTemplateId = doodad is { } d ? new DoodadTemplateId(d) : null,
            },
        ];

        Assert.Throws<DbUpdateException>(() => Save(quest));
    }

    [Fact]
    public void Refuse_an_objective_with_a_count_of_zero()
    {
        QuestTemplate quest = Quest(9003);
        quest.Objectives =
        [
            new QuestObjective
            {
                Id = 90031, QuestId = 9003, StageSequence = 0, Type = QuestObjectiveType.Kill,
                CreatureTemplateId = 4, Count = 0, DescriptionTextId = 1,
            },
        ];

        Assert.Throws<DbUpdateException>(() => Save(quest));
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(100.5f)]
    public void Refuse_a_drop_chance_outside_zero_to_one_hundred(float chance)
    {
        QuestTemplate quest = Quest(9004);
        quest.Objectives =
        [
            new QuestObjective
            {
                Id = 90041, QuestId = 9004, StageSequence = 0, Type = QuestObjectiveType.Collect,
                ItemTemplateId = 1, Count = 1, DescriptionTextId = 1,
                Drops = [new QuestItemDrop { ObjectiveId = 90041, CreatureTemplateId = 4, Chance = chance }],
            },
        ];

        Assert.Throws<DbUpdateException>(() => Save(quest));
    }

    [Fact]
    public void Keep_the_quest_item_flag_value() => Assert.Equal(2048, (int)ItemTemplateFlags.QuestItem);
}
