using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #434: a level-up recalculates stats at the new level and refills health and power. Driven
/// through a real kill in a real MapInstance, because the level-up lives in CreatureKilled.
/// </summary>
public class LevelUpStatsShould
{
    private static readonly ClassLevelStat[] s_warriorRows =
    [
        new() { Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, BaseMana = 0, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 },
        new() { Class = CharacterClass.Warrior, Level = 2, BaseHp = 40, BaseMana = 0, Stamina = 24, Strength = 25, Agility = 21, Intellect = 20 },
    ];

    [Fact]
    public async Task Recalculate_at_the_new_level_and_refill_on_a_level_up()
    {
        StaticData data = await TestStaticData.LoadAsync(
            classStats: s_warriorRows,
            levels:
            [
                new CharacterLevelExperience { Level = 1, Experience = 100 },
                new CharacterLevelExperience { Level = 2, Experience = 500 },
                new CharacterLevelExperience { Level = 3, Experience = 900 },   // level 2 is not the maximum (#735)
            ]);
        IWorld world = Substitute.For<IWorld>();
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

        instance.ReportKill(creature, killer);

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
    /// #526 end to end: a Warrior's Cleave kills a creature and the kill levels it up through the real
    /// CreatureKilled. The refill keeps Fury, capped at the new maximum (100 here), and the kill's own
    /// Cleave gain (8) is counted: 60 becomes 68, and 150 of a former 200 ends at 100.
    /// </summary>
    [Theory]
    [InlineData(100u, 60u, 68u)]
    [InlineData(200u, 150u, 100u)]
    public async Task Keep_a_warriors_fury_capped_through_a_level_up_its_cleave_caused(uint maxBefore, uint furyBefore, uint furyAfter)
    {
        StaticData data = await TestStaticData.LoadAsync(
            classStats: s_warriorRows,
            levels:
            [
                new CharacterLevelExperience { Level = 1, Experience = 100 },
                new CharacterLevelExperience { Level = 2, Experience = 500 },
            ]);
        IWorld world = MapInstanceClients.NewWorld(data);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, world: world);

        MapInstanceClient warrior = MapInstanceClients.Join(instance, 526_401);
        warrior.Character.PowerType = PowerType.Fury;
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, data, CurrentValues.EnterWorld));
        warrior.Character.Power = maxBefore;
        warrior.Character.CurrentPower = furyBefore;
        warrior.Character.Orientation = new Vector3(0f, 0f, 0f);   // facing +Z
        AbilityTemplate cleave = AbilityTestData.Cone(200, reach: 2.5f, arc: 100f);
        cleave.PowerGainPerHit = 8;
        warrior.Character.Spells.Load([AbilityTestData.Game(cleave)]);

        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 526_901u + furyBefore),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = new Vector3(0f, 0f, 2f),
            Health = 5,
            CurrentHealth = 5,
            Experience = 150,
        };
        creature.Script = new MapInstanceAbilityCastShould.WoundScript(creature);
        instance.AddCreature(creature);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(0u, creature.CurrentHealth);
        Assert.Equal((ushort)2, warrior.Character.Level);
        Assert.Equal(100u, warrior.Character.Power);
        Assert.Equal(warrior.Character.Health, warrior.Character.CurrentHealth);
        Assert.Equal(furyAfter, warrior.Character.CurrentPower);
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
            classStats: s_warriorRows,
            levels:
            [
                new CharacterLevelExperience { Level = 1, Experience = 100 },
                new CharacterLevelExperience { Level = 2, Experience = 500 },
            ]);
        IWorld world = Substitute.For<IWorld>();
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

        instance.ReportKill(creature, killer);

        Assert.Equal((ushort)2, killer.Level);
        Assert.Equal(40u + 24u * 10u, killer.Health);
        Assert.True(killer.IsDead);
        Assert.Equal(0u, killer.CurrentHealth);
        instance.Dispose();
    }
}
