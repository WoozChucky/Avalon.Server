using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Movement;
using Avalon.Server.World.UnitTests.Reload;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// #627: a character's movement speed is the base 4 m/s plus the gear's MovementSpeed percentage,
/// clamped to the combat formula's floor and cap, recalculated at every stats refresh.
/// </summary>
public class CharacterMovementSpeedShould
{
    private static readonly CombatFormula Seeded = CombatSeed.Formula();

    private static readonly ClassLevelStat[] WarriorRows =
    [
        new() { Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, BaseMana = 0, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 },
    ];

    private static readonly ItemTemplate SwiftBoots = new()
    {
        Id = new ItemTemplateId(627_001), Name = "Swift Boots", Slot = ItemSlotType.Feet, MaxStackSize = 1,
        StatType1 = StatType.MovementSpeed, StatValue1 = 10,
    };

    private static DerivedCharacterStats Stats(float movementPct) =>
        new(MaxHealth: 100, MaxPower: 100, Stamina: 0, Strength: 0, Agility: 0, Intellect: 0, Armor: 0,
            BlockPct: 0f, DodgePct: 0f, CritPct: 0f, AttackDamage: 0, AbilityDamage: 0, MovementSpeedPct: movementPct);

    private static ItemTemplate? Find(ItemTemplateId id) => id == SwiftBoots.Id ? SwiftBoots : null;

    [Fact]
    public void Move_at_the_base_four_metres_a_second_with_no_gear()
    {
        CharacterEntity character = New();
        Assert.Equal(4f, character.GetMovementSpeed());

        Assert.True(CharacterStatsRefresh.Apply(character, WarriorRows, TestCombat.Factors, Find, CurrentValues.EnterWorld, Seeded));

        Assert.Equal(4f, character.GetMovementSpeed());
    }

    [Theory]
    [InlineData(10f, 4.4f)]
    [InlineData(50f, 6.0f)]
    [InlineData(80f, 6.0f)]    // clamped to the +50 % cap
    [InlineData(-50f, 2.0f)]
    [InlineData(-80f, 2.0f)]   // clamped to the -50 % floor
    [InlineData(0f, 4.0f)]
    public void Add_the_gear_percentage_to_the_base_within_the_formulas_bounds(float pct, float speed)
    {
        CharacterEntity character = New();

        character.ApplyStats(Stats(pct), CurrentValues.EnterWorld, Seeded);

        Assert.Equal(speed, character.GetMovementSpeed(), precision: 5);
    }

    [Fact]
    public void Follow_the_formulas_own_bounds()
    {
        CharacterEntity character = New();
        CombatFormula tight = CombatSeed.Formula();
        tight.MoveSpeedCap = 20f;
        tight.MoveSpeedFloor = -10f;

        character.ApplyStats(Stats(80f), CurrentValues.EnterWorld, tight);
        Assert.Equal(4.8f, character.GetMovementSpeed(), precision: 5);

        character.ApplyStats(Stats(-80f), CurrentValues.KeepShare, tight);
        Assert.Equal(3.6f, character.GetMovementSpeed(), precision: 5);
    }

    [Fact]
    public void Change_the_speed_at_the_refresh_after_a_gear_change()
    {
        CharacterEntity character = New();
        Assert.True(CharacterStatsRefresh.Apply(character, WarriorRows, TestCombat.Factors, Find, CurrentValues.EnterWorld, Seeded));

        character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Feet, SwiftBoots)]);
        Assert.Equal(4f, character.GetMovementSpeed());
        Assert.True(CharacterStatsRefresh.Apply(character, WarriorRows, TestCombat.Factors, Find, CurrentValues.KeepShare, Seeded));
        Assert.Equal(4.4f, character.GetMovementSpeed(), precision: 5);

        character.Container(InventoryType.Equipment).Load([]);
        Assert.True(CharacterStatsRefresh.Apply(character, WarriorRows, TestCombat.Factors, Find, CurrentValues.KeepShare, Seeded));
        Assert.Equal(4f, character.GetMovementSpeed(), precision: 5);
    }

    /// <summary>The refresh over StaticData reads the current generation's formula: a combat reload moves the bounds.</summary>
    [Fact]
    public async Task Use_the_reloaded_bounds_at_the_next_refresh()
    {
        var rows = new CombatReloadShould.Rows();
        StaticData data = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            classStats: () => WarriorRows, items: () => [SwiftBoots], combat: rows.Repository()));
        CharacterEntity character = New();
        character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Feet, SwiftBoots)]);
        Assert.True(CharacterStatsRefresh.Apply(character, data, CurrentValues.EnterWorld));
        Assert.Equal(4.4f, character.GetMovementSpeed(), precision: 5);

        rows.Formulas[0].MoveSpeedCap = 5f;
        data.Apply(await data.PrepareAsync(ReloadArea.Combat));
        Assert.Equal(4.4f, character.GetMovementSpeed(), precision: 5);

        Assert.True(CharacterStatsRefresh.Apply(character, data, CurrentValues.KeepShare));
        Assert.Equal(4.2f, character.GetMovementSpeed(), precision: 5);
    }

    /// <summary>
    /// Review focus 4: the client learns a new speed with the sheet, a round trip (taken as 100 ms) after the
    /// server starts stepping at it, and snaps past 0.15 m of drift. +10 % at 4 m/s drifts 0.04 m. Equipping
    /// or removing any one seeded item must stay under the snap too, so an item seeded with a big bonus
    /// fails here and makes someone look (moving from the base to the +50 % cap in one change drifts 0.2 m).
    /// </summary>
    [Fact]
    public void Keep_the_drift_of_a_speed_change_over_one_round_trip_under_the_clients_snap()
    {
        const float roundTrip = 0.1f;
        float Drift(float fromPct, float toPct) =>
            MathF.Abs(CharacterMovement.SpeedFor(toPct, Seeded) - CharacterMovement.SpeedFor(fromPct, Seeded)) * roundTrip;

        Assert.Equal(0.04f, Drift(0f, 10f), precision: 5);
        Assert.True(Drift(0f, 10f) < CharacterMovement.ClientSnapThreshold);

        using Handlers.SqliteDatabase<Avalon.Database.World.WorldDbContext> database = Handlers.SqliteDatabase.World();
        using Avalon.Database.World.WorldDbContext context = database.CreateDbContext();
        foreach (ItemTemplate item in context.ItemTemplates.ToList())
        {
            float bonus = new (StatType? Type, uint? Value)[]
                {
                    (item.StatType1, item.StatValue1), (item.StatType2, item.StatValue2), (item.StatType3, item.StatValue3),
                    (item.StatType4, item.StatValue4), (item.StatType5, item.StatValue5), (item.StatType6, item.StatValue6),
                    (item.StatType7, item.StatValue7), (item.StatType8, item.StatValue8), (item.StatType9, item.StatValue9),
                    (item.StatType10, item.StatValue10),
                }
                .Where(s => s.Type == StatType.MovementSpeed)
                .Sum(s => (float)(s.Value ?? 0));
            Assert.True(Drift(0f, bonus) < CharacterMovement.ClientSnapThreshold,
                $"item {item.Id.Value} ({item.Name}) moves the speed {Drift(0f, bonus)} m off over a round trip");
        }
    }

    /// <summary>The input step reads the character's current speed: +10 % gear moves 4.4 x 1/60 m a tick.</summary>
    [Fact]
    public void Step_at_the_derived_speed()
    {
        CharacterEntity character = New();
        character.InstanceId = Guid.NewGuid();
        character.ApplyStats(Stats(10f), CurrentValues.EnterWorld, Seeded);

        var navigator = Substitute.For<IMapNavigator>();
        navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(call => call.ArgAt<Vector3>(1));
        navigator.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(0f);
        var instance = Substitute.For<IMapInstance>();
        instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(navigator);
        var world = Substitute.For<IWorld>();
        world.InstanceRegistry.GetInstanceById(character.InstanceId).Returns(instance);
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());

        new PlayerInputHandler(NullLogger<PlayerInputHandler>.Instance, world)
            .Execute(connection, new CPlayerInputPacket { Seq = 1, DirX = 1, DirZ = 0 });

        Assert.Equal(4.4f / 60f, character.Position.x, precision: 5);
    }
}
