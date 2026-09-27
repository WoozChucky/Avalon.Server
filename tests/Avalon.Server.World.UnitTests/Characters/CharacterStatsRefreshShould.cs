using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>Writing derived stats to the character, and working out what it is wearing.</summary>
public class CharacterStatsRefreshShould
{
    private static readonly ClassLevelStat WarriorLevel1 = new()
    {
        Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, BaseMana = 0,
        Stamina = 22, Strength = 23, Agility = 20, Intellect = 20,
    };

    private static readonly ClassLevelStat WizardLevel1 = new()
    {
        Class = CharacterClass.Wizard, Level = 1, BaseHp = 16, BaseMana = 20,
        Stamina = 21, Strength = 20, Agility = 20, Intellect = 23,
    };

    private static readonly ClassLevelStat[] Rows = [WarriorLevel1, WizardLevel1];

    [Fact]
    public void Refill_both_pools_and_mark_the_stats_for_the_next_save()
    {
        CharacterEntity character = New();
        character.ConsumeDirtyFields();

        Assert.True(CharacterStatsRefresh.Apply(character, Rows, TestCombat.Factors, EquipTemplates.Find, CurrentValues.Refill));

        Assert.Equal(240u, character.Health);
        Assert.Equal(240u, character.CurrentHealth);
        Assert.Equal(100u, character.Power);
        Assert.Equal(100u, character.CurrentPower);
        Assert.Equal(22u, character.Stamina);
        Assert.Equal(240, character.Data!.Health);
        Assert.Equal(100, character.Data.Power1);
        Assert.True(character.SaveState.StatsDirty);
        Assert.Equal(240u, character.Stats!.Value.MaxHealth);

        GameEntityFields dirty = character.ConsumeDirtyFields();
        Assert.True(dirty.HasFlag(GameEntityFields.Health));
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentHealth));
        Assert.True(dirty.HasFlag(GameEntityFields.Power));
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentPower));
    }

    [Fact]
    public void Keep_the_share_of_health_when_gear_goes_on()
    {
        CharacterEntity character = New();
        CharacterStatsRefresh.Apply(character, Rows, TestCombat.Factors, EquipTemplates.Find, CurrentValues.Refill);
        character.CurrentHealth = 120;
        character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Chest, EquipTemplates.Chestguard)]);

        Assert.True(CharacterStatsRefresh.Apply(character, Rows, TestCombat.Factors, EquipTemplates.Find, CurrentValues.KeepShare));

        Assert.Equal(260u, character.Health);
        Assert.Equal(130u, character.CurrentHealth);
        Assert.Equal(100u, character.CurrentPower);
    }

    /// <summary>Review Focus 5.</summary>
    [Fact]
    public void Count_only_what_is_worn_in_a_slot_it_fits()
    {
        CharacterEntity character = New();
        character.Container(InventoryType.Equipment).Load(
        [
            Item(EquipmentSlots.Chest, EquipTemplates.Chestguard),   // fits: counts
            Item(EquipmentSlots.Finger2, EquipTemplates.Ruby),       // a gem in slot 8: does not
            Item(12, EquipTemplates.Longsword),                      // a reserved slot: does not
            Item(EquipmentSlots.Head, EquipTemplates.Ghost),         // template gone: does not
        ]);
        character.Container(InventoryType.Bag).Load([Item(0, EquipTemplates.Greatsword)]);

        Assert.True(CharacterStatsRefresh.Apply(character, Rows, TestCombat.Factors, EquipTemplates.Find, CurrentValues.Refill));

        Assert.Equal(25u, character.Stats!.Value.Strength);
        Assert.Equal(24u, character.Stats.Value.Stamina);
        Assert.Equal(8u, character.Stats.Value.Armor);
    }

    [Fact]
    public void Change_nothing_when_the_class_and_level_have_no_row()
    {
        CharacterEntity character = New();
        character.Level = 9;
        character.Data!.Health = 150;
        character.CurrentHealth = 150;

        Assert.False(CharacterStatsRefresh.Apply(character, Rows, TestCombat.Factors, EquipTemplates.Find, CurrentValues.Refill));

        Assert.Equal(150u, character.Health);
        Assert.Equal(150u, character.CurrentHealth);
        Assert.Null(character.Stats);
        Assert.False(character.SaveState.StatsDirty);
    }

    [Theory]
    [InlineData(CharacterClass.Warrior, 0u)]
    [InlineData(CharacterClass.Wizard, 23u)]
    public void Regenerate_from_the_class_regen_attribute(CharacterClass @class, uint regen)
    {
        CharacterEntity character = New();
        character.Data!.Class = @class;

        CharacterStatsRefresh.Apply(character, Rows, TestCombat.Factors, EquipTemplates.Find, CurrentValues.Refill);

        Assert.Equal(regen, character.RegenStat);
    }

    /// <summary>A warrior of <see cref="WarriorLevel1" /> on a Fury pool holding <paramref name="fury" /> of <paramref name="max" />.</summary>
    private static CharacterEntity FuryWarrior(uint fury, uint max = 100)
    {
        CharacterEntity warrior = New();
        warrior.PowerType = Avalon.Network.Packets.State.PowerType.Fury;
        warrior.Power = max;
        warrior.CurrentPower = fury;
        warrior.CurrentHealth = 1;
        return warrior;
    }

    /// <summary>#526: select fills health and the fill-up pools, and a warrior enters with no Fury.</summary>
    [Fact]
    public void Enter_The_World_With_No_Fury_And_Full_Mana()
    {
        CharacterEntity warrior = FuryWarrior(fury: 30);
        CharacterEntity wizard = New();
        wizard.Data!.Class = CharacterClass.Wizard;
        wizard.PowerType = Avalon.Network.Packets.State.PowerType.Mana;
        wizard.CurrentPower = 0;

        Assert.True(CharacterStatsRefresh.Apply(warrior, Rows, TestCombat.Factors, EquipTemplates.Find, CurrentValues.EnterWorld));
        Assert.True(CharacterStatsRefresh.Apply(wizard, Rows, TestCombat.Factors, EquipTemplates.Find, CurrentValues.EnterWorld));

        Assert.Equal(0u, warrior.CurrentPower);
        Assert.Equal(warrior.Health, warrior.CurrentHealth);
        Assert.True(wizard.Power > 0);
        Assert.Equal(wizard.Power, wizard.CurrentPower);
        Assert.Equal(wizard.Health, wizard.CurrentHealth);
    }

    /// <summary>#526 review focus: a level-up refills health but leaves a warrior's Fury where it was.</summary>
    [Fact]
    public void Keep_Fury_On_A_Level_Up_Refill()
    {
        CharacterEntity warrior = FuryWarrior(fury: 60);

        Assert.True(CharacterStatsRefresh.Apply(warrior, Rows, TestCombat.Factors, EquipTemplates.Find, CurrentValues.Refill));

        Assert.Equal(100u, warrior.Power);
        Assert.Equal(60u, warrior.CurrentPower);
        Assert.Equal(240u, warrior.CurrentHealth);
    }

    [Theory]
    [InlineData(CurrentValues.Refill)]
    [InlineData(CurrentValues.KeepShare)]
    public void Cap_Fury_When_The_Maximum_Shrinks(CurrentValues current)
    {
        CharacterEntity warrior = FuryWarrior(fury: 150, max: 200);

        Assert.True(CharacterStatsRefresh.Apply(warrior, Rows, TestCombat.Factors, EquipTemplates.Find, current));

        Assert.Equal(100u, warrior.Power);
        Assert.Equal(100u, warrior.CurrentPower);
    }

    /// <summary>#526: Fury is not a fill-up pool, so a gear change keeps its value, not its share.</summary>
    [Fact]
    public void Keep_Fury_On_A_Gear_Change()
    {
        CharacterEntity warrior = FuryWarrior(fury: 60, max: 200);

        Assert.True(CharacterStatsRefresh.Apply(warrior, Rows, TestCombat.Factors, EquipTemplates.Find, CurrentValues.KeepShare));

        Assert.Equal(100u, warrior.Power);
        Assert.Equal(60u, warrior.CurrentPower);   // a share would have been 30
    }

    [Fact]
    public void Regenerate_a_hunter_from_agility()
    {
        CharacterEntity character = New();
        character.Data!.Class = CharacterClass.Hunter;
        ClassLevelStat hunter = new()
        {
            Class = CharacterClass.Hunter, Level = 1, BaseHp = 18, BaseMana = 10,
            Stamina = 20, Strength = 21, Agility = 23, Intellect = 20,
        };

        CharacterStatsRefresh.Apply(character, [hunter], TestCombat.Factors, EquipTemplates.Find, CurrentValues.Refill);

        Assert.Equal(23u, character.RegenStat);
    }
}
