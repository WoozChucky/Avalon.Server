using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #434: a level-up recalculates stats at the new level and refills health and power. Driven
/// through a real kill in a real MapInstance, because the level-up lives in OnCreatureKilled.
/// </summary>
public class LevelUpStatsShould
{
    private static readonly ClassLevelStat[] WarriorRows =
    [
        new() { Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, BaseMana = 0, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 },
        new() { Class = CharacterClass.Warrior, Level = 2, BaseHp = 40, BaseMana = 0, Stamina = 24, Strength = 25, Agility = 21, Intellect = 20 },
    ];

    [Fact]
    public async Task Recalculate_at_the_new_level_and_refill_on_a_level_up()
    {
        StaticData data = await TestStaticData.LoadAsync(
            classStats: WarriorRows,
            levels:
            [
                new CharacterLevelExperience { Level = 1, Experience = 100 },
                new CharacterLevelExperience { Level = 2, Experience = 500 },
            ]);
        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        world.MapTemplates.Returns(new List<MapTemplate> { new() { Id = new MapTemplateId(1), Name = "Town" } });
        world.Data.Returns(data);
        MapInstance instance = TestMapInstances.Build(world);

        CharacterEntity killer = TestCharacters.New();
        CharacterStatsRefresh.Apply(killer, data, CurrentValues.Refill);
        killer.CurrentHealth = 10;
        killer.CurrentPower = 5;

        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 434_001),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Experience = 150,
        };
        instance.AddCreature(creature);

        creature.Died(killer);

        Assert.Equal((ushort)2, killer.Level);
        Assert.Equal(50ul, killer.Experience);
        Assert.Equal(40u + 24u * 10u, killer.Health);
        Assert.Equal(killer.Health, killer.CurrentHealth);
        Assert.Equal(100u, killer.CurrentPower);
        Assert.Equal(50u, killer.Stats!.Value.AttackDamage);
        Assert.True(killer.SaveState.StatsDirty);
        instance.Dispose();
    }

    /// <summary>
    /// #463 final review: against the seeded rows, not hand-written ones. The seed once stopped at
    /// level 5, so a level-up to 6 found no row and left health where it was; 16 is the highest
    /// level the seeded experience table lets a character reach. Warrior health is BaseHp + 10 per
    /// Stamina: 400 at 5, 440 at 6, 800 at 15, 840 at 16.
    /// </summary>
    [Theory]
    [InlineData(5, 400u, 440u)]
    [InlineData(15, 800u, 840u)]
    public async Task Raise_and_refill_health_on_a_level_up_past_level_5_with_the_seeded_rows(
        int fromLevel, uint healthBefore, uint healthAfter)
    {
        List<ClassLevelStat> classStats;
        List<CharacterLevelExperience> levels;
        using (SqliteDatabase<WorldDbContext> database = SqliteDatabase.World())
        using (WorldDbContext context = database.CreateDbContext())
        {
            classStats = context.ClassLevelStats.AsNoTracking().ToList();
            levels = context.CharacterLevelExperiences.AsNoTracking().ToList();
        }

        StaticData data = await TestStaticData.LoadAsync(classStats: classStats, levels: levels);
        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        world.MapTemplates.Returns(new List<MapTemplate> { new() { Id = new MapTemplateId(1), Name = "Town" } });
        world.Data.Returns(data);
        MapInstance instance = TestMapInstances.Build(world);

        CharacterEntity killer = TestCharacters.New();
        killer.Level = (ushort)fromLevel;
        Assert.True(CharacterStatsRefresh.Apply(killer, data, CurrentValues.Refill));
        Assert.Equal(healthBefore, killer.Health);
        killer.Experience = levels.Single(l => l.Level == fromLevel).Experience - 1;
        killer.CurrentHealth = 10;

        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 434_010u + (uint)fromLevel),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Experience = 1,
        };
        instance.AddCreature(creature);

        creature.Died(killer);

        Assert.Equal((ushort)(fromLevel + 1), killer.Level);
        Assert.Equal(healthAfter, killer.Health);
        Assert.Equal(healthAfter, killer.CurrentHealth);
        instance.Dispose();
    }

    /// <summary>
    /// A kill can land after its killer has died (a projectile in flight, an ability still
    /// ticking). The level-up still raises the maximums, but it must not refill a corpse: a dead
    /// character stays dead at 0 health.
    /// </summary>
    [Fact]
    public async Task Level_up_a_dead_killer_without_refilling_its_corpse()
    {
        StaticData data = await TestStaticData.LoadAsync(
            classStats: WarriorRows,
            levels:
            [
                new CharacterLevelExperience { Level = 1, Experience = 100 },
                new CharacterLevelExperience { Level = 2, Experience = 500 },
            ]);
        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        world.MapTemplates.Returns(new List<MapTemplate> { new() { Id = new MapTemplateId(1), Name = "Town" } });
        world.Data.Returns(data);
        MapInstance instance = TestMapInstances.Build(world);

        CharacterEntity killer = TestCharacters.New();
        CharacterStatsRefresh.Apply(killer, data, CurrentValues.Refill);
        killer.CurrentHealth = 0;
        killer.IsDead = true;

        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 434_003),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Experience = 150,
        };
        instance.AddCreature(creature);

        creature.Died(killer);

        Assert.Equal((ushort)2, killer.Level);
        Assert.Equal(40u + 24u * 10u, killer.Health);
        Assert.True(killer.IsDead);
        Assert.Equal(0u, killer.CurrentHealth);
        instance.Dispose();
    }

    /// <summary>
    /// Regression guard: this passes without the level-up refresh too, because before it nothing
    /// touched the maximums on a level-up. It pins that a new level with no ClassLevelStat row
    /// leaves the old maximums alone rather than zeroing them.
    /// </summary>
    [Fact]
    public async Task Level_up_and_keep_the_old_maximums_when_the_new_level_has_no_row()
    {
        StaticData data = await TestStaticData.LoadAsync(
            classStats: [WarriorRows[0]],
            levels: [new CharacterLevelExperience { Level = 1, Experience = 100 }]);
        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        world.MapTemplates.Returns(new List<MapTemplate> { new() { Id = new MapTemplateId(1), Name = "Town" } });
        world.Data.Returns(data);
        MapInstance instance = TestMapInstances.Build(world);

        CharacterEntity killer = TestCharacters.New();
        CharacterStatsRefresh.Apply(killer, data, CurrentValues.Refill);
        killer.CurrentHealth = 90;

        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 434_002),
            Metadata = Substitute.For<ICreatureMetadata>(),
            Experience = 150,
        };
        instance.AddCreature(creature);

        creature.Died(killer);

        Assert.Equal((ushort)2, killer.Level);
        Assert.Equal(240u, killer.Health);
        Assert.Equal(90u, killer.CurrentHealth);
        instance.Dispose();
    }
}
