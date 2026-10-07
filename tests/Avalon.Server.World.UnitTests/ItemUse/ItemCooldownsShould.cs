using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Items;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>Item use cooldowns: per item and per group, in memory, measured on absolute time.</summary>
public class ItemCooldownsShould
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly ItemTemplateId Tonic = new(700);
    private static readonly ItemTemplateId Elixir = new(701);

    [Fact]
    public void Have_nothing_running_at_first()
    {
        Assert.Equal(TimeSpan.Zero, new ItemCooldowns().Remaining(Tonic, "potion", Now));
    }

    [Fact]
    public void Count_down_an_items_own_cooldown()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(Tonic, group: null, TimeSpan.FromSeconds(30), Now);

        Assert.Equal(TimeSpan.FromSeconds(20), cooldowns.Remaining(Tonic, null, Now.AddSeconds(10)));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(Tonic, null, Now.AddSeconds(30)));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(Elixir, null, Now.AddSeconds(10)));
    }

    [Fact]
    public void Share_a_groups_cooldown_between_items()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(Tonic, "potion", TimeSpan.FromSeconds(30), Now);

        Assert.Equal(TimeSpan.FromSeconds(30), cooldowns.Remaining(Elixir, "potion", Now));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(Elixir, "scroll", Now));
    }

    [Fact]
    public void Answer_the_longer_of_the_item_and_its_group()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(Tonic, null, TimeSpan.FromSeconds(60), Now);
        cooldowns.Start(Elixir, "potion", TimeSpan.FromSeconds(10), Now);

        Assert.Equal(TimeSpan.FromSeconds(60), cooldowns.Remaining(Tonic, "potion", Now));
    }

    /// <summary>The group is the longer: an item off its own cooldown still waits for its group.</summary>
    [Fact]
    public void Answer_the_groups_cooldown_when_it_is_the_longer()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(Tonic, "potion", TimeSpan.FromSeconds(10), Now);
        cooldowns.Start(Elixir, "potion", TimeSpan.FromSeconds(60), Now);

        Assert.Equal(TimeSpan.FromSeconds(50), cooldowns.Remaining(Tonic, "potion", Now.AddSeconds(10)));
    }

    /// <summary>A blank group is no group: it shares nothing with another blank one, and a blank lookup sees none.</summary>
    [Fact]
    public void Count_a_blank_group_as_no_group()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(Tonic, "   ", TimeSpan.FromSeconds(30), Now);

        Assert.Equal(TimeSpan.FromSeconds(30), cooldowns.Remaining(Tonic, "   ", Now));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(Elixir, "   ", Now));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(Elixir, "", Now));
    }

    [Fact]
    public void Start_nothing_for_no_duration_or_a_blank_group()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(Tonic, "  ", TimeSpan.Zero, Now);
        cooldowns.Start(Elixir, "", TimeSpan.FromSeconds(5), Now);

        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(Tonic, null, Now));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(Tonic, "", Now));
    }

    [Fact]
    public void Live_on_the_character_and_start_empty_with_every_new_session()
    {
        CharacterEntity character = Inventory.TestCharacters.New(7);
        character.ItemCooldowns.Start(Tonic, null, TimeSpan.FromSeconds(30), Now);

        Assert.Equal(TimeSpan.Zero, Inventory.TestCharacters.New(7).ItemCooldowns.Remaining(Tonic, null, Now));
    }
}
