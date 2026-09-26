using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
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
        instance.Dispose();
    }
}
