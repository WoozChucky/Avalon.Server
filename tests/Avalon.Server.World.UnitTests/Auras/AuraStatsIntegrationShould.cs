using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>What auras do to a unit's stats: a character's refresh folds them in, a creature folds them on the spot.</summary>
public class AuraStatsIntegrationShould
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static ActiveAura Held(AuraTemplate template, uint stacks = 1) =>
        new(template, new ObjectGuid(), AuraSource.None, stacks, default,
            AuraSchedule.Start(T0, template.DurationMs, template.TickIntervalMs), template.DurationMs, T0.UtcDateTime);

    private static readonly ClassLevelStat WarriorOne = new()
    { Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 };

    private static readonly ItemTemplate Plate = new()
    {
        Id = new ItemTemplateId(905_001),
        Name = "Plate",
        Slot = ItemSlotType.Chest,
        MaxStackSize = 1,
        StatType1 = StatType.Armor,
        StatValue1 = 30,
    };

    private static CharacterEntity Warrior()
    {
        CharacterEntity warrior = TestCharacters.New(905_101);
        warrior.Container(InventoryType.Equipment).Load([TestCharacters.Item(3, Plate)]);
        Assert.True(CharacterStatsRefresh.Apply(warrior, [WarriorOne], TestCombat.Factors,
            id => id == Plate.Id ? Plate : null, CurrentValues.EnterWorld, TestCombat.Formula));
        return warrior;
    }

    [Fact]
    public void Fold_a_characters_auras_into_its_next_stats_refresh()
    {
        CharacterEntity warrior = Warrior();
        Assert.Equal(30u, warrior.Stats!.Value.Armor);

        warrior.Auras.Add(Held(AuraTestData.Fortified()), T0);
        CharacterStatsRefresh.Apply(warrior, [WarriorOne], TestCombat.Factors, id => id == Plate.Id ? Plate : null,
            CurrentValues.KeepShare, TestCombat.Formula);

        Assert.Equal(36u, warrior.Stats!.Value.Armor);
        Assert.True(warrior.SaveState.StatsDirty);
    }

    /// <summary>Crippled on a gearless character: 4 m/s x (1 - 30 / 100) = 2.8 m/s.</summary>
    [Fact]
    public void Slow_a_crippled_character()
    {
        CharacterEntity warrior = Warrior();
        warrior.Auras.Add(Held(AuraTestData.Crippled()), T0);

        CharacterStatsRefresh.Apply(warrior, [WarriorOne], TestCombat.Factors, id => id == Plate.Id ? Plate : null,
            CurrentValues.KeepShare, TestCombat.Formula);

        Assert.Equal(2.8f, warrior.GetMovementSpeed(), precision: 4);
    }

    private static Creature Wolf() => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, 905_901),
        Level = 3,
        Health = 100,
        CurrentHealth = 100,
        BaseMaxHealth = 100,
        Armor = 40,
        CritPct = 5f,
        DodgePct = 2f,
        BlockPct = 1f,
        DamageMin = 5,
        DamageMax = 9,
        Speed = 4f,
    };

    [Fact]
    public void Fold_a_creatures_auras_into_its_defence_on_the_spot()
    {
        Creature wolf = Wolf();
        AuraTemplate sundered = AuraTestData.Fortified(907);
        sundered.Kind = AuraKind.Harmful;
        sundered.Modifiers[0].Value = -25f;
        wolf.Auras.Add(Held(sundered), T0);

        AuraStatsRefresh.Apply(wolf, data: null);

        Assert.Equal(new DefenderCombat(30, 2f, 1f), Internal<DefenderCombat>(wolf, "Defence"));
    }

    /// <summary>A creature's slow reaches the locomotion through its speed factor; the script's Speed stays the template's.</summary>
    [Fact]
    public void Slow_a_crippled_creature_and_bound_it_by_the_formula()
    {
        Creature wolf = Wolf();
        wolf.Auras.Add(Held(AuraTestData.Crippled()), T0);
        AuraStatsRefresh.Apply(wolf, data: null);

        Assert.Equal(0.7f, wolf.SpeedFactor, precision: 4);
        Assert.Equal(2.8f, CreatureSpeed.Of(wolf), precision: 4);
        Assert.Equal(4f, wolf.Speed);

        wolf.Auras.Add(Held(AuraTestData.Crippled(908), stacks: 1), T0);
        wolf.Auras.Add(Held(AuraTestData.Crippled(909), stacks: 1), T0);
        AuraStatsRefresh.Apply(wolf, data: null);
        Assert.Equal(0.5f, wolf.SpeedFactor, precision: 4);   // -90 points, held at the -50 floor
    }

    [Fact]
    public void Walk_a_crippled_creature_at_its_slowed_speed()
    {
        var navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns([new Vector3(20f, 0f, 0f)]);
        var locomotion = new WaypointLocomotion(_ => navigator);
        Creature wolf = Wolf();
        wolf.Position = Vector3.zero;
        wolf.Auras.Add(Held(AuraTestData.Crippled()), T0);
        AuraStatsRefresh.Apply(wolf, data: null);

        locomotion.Register(wolf, radius: 0.5f);
        locomotion.MoveTo(wolf, new Vector3(20f, 0f, 0f));
        locomotion.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(2.8f, wolf.Position.x, precision: 3);
        Assert.Equal(2.8f, wolf.Velocity.magnitude, precision: 3);
    }

    /// <summary>A party rescale and a health aura compose: 100 x 1.6 x 1.1 = 176, keeping the share of health held.</summary>
    [Fact]
    public void Compose_a_creatures_health_aura_with_its_party_scaling()
    {
        Creature wolf = Wolf();
        wolf.CurrentHealth = 50;
        AuraTemplate vigour = AuraTestData.Fortified(910);
        vigour.Modifiers[0].Stat = AuraStat.MaxHealth;
        vigour.Modifiers[0].Value = 10f;

        wolf.Rescale(1.6);
        wolf.Auras.Add(Held(vigour), T0);
        AuraStatsRefresh.Apply(wolf, data: null);

        Assert.Equal(176u, wolf.Health);
        Assert.Equal(88u, wolf.CurrentHealth);
    }

    /// <summary>Reads a World-side internal member (Combat or Defence), as SimulatorParityShould does.</summary>
    private static T Internal<T>(object unit, string property) =>
        (T)unit.GetType().GetProperty(property, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(unit)!;
}
