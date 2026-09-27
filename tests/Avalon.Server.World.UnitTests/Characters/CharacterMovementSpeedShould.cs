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
