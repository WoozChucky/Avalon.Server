using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Items;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>Item use cooldowns: per item and per group, in memory, measured on absolute time.</summary>
public class ItemCooldownsShould
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly ItemTemplateId s_tonic = new(700);
    private static readonly ItemTemplateId s_elixir = new(701);

    [Fact]
    public void Have_nothing_running_at_first()
    {
        Assert.Equal(TimeSpan.Zero, new ItemCooldowns().Remaining(s_tonic, "potion", s_now));
    }

    [Fact]
    public void Count_down_an_items_own_cooldown()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(s_tonic, group: null, TimeSpan.FromSeconds(30), s_now);

        Assert.Equal(TimeSpan.FromSeconds(20), cooldowns.Remaining(s_tonic, null, s_now.AddSeconds(10)));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(s_tonic, null, s_now.AddSeconds(30)));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(s_elixir, null, s_now.AddSeconds(10)));
    }

    [Fact]
    public void Share_a_groups_cooldown_between_items()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(s_tonic, "potion", TimeSpan.FromSeconds(30), s_now);

        Assert.Equal(TimeSpan.FromSeconds(30), cooldowns.Remaining(s_elixir, "potion", s_now));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(s_elixir, "scroll", s_now));
    }

    [Fact]
    public void Answer_the_longer_of_the_item_and_its_group()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(s_tonic, null, TimeSpan.FromSeconds(60), s_now);
        cooldowns.Start(s_elixir, "potion", TimeSpan.FromSeconds(10), s_now);

        Assert.Equal(TimeSpan.FromSeconds(60), cooldowns.Remaining(s_tonic, "potion", s_now));
    }

    /// <summary>The group is the longer: an item off its own cooldown still waits for its group.</summary>
    [Fact]
    public void Answer_the_groups_cooldown_when_it_is_the_longer()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(s_tonic, "potion", TimeSpan.FromSeconds(10), s_now);
        cooldowns.Start(s_elixir, "potion", TimeSpan.FromSeconds(60), s_now);

        Assert.Equal(TimeSpan.FromSeconds(50), cooldowns.Remaining(s_tonic, "potion", s_now.AddSeconds(10)));
    }

    /// <summary>A blank group is no group: it shares nothing with another blank one, and a blank lookup sees none.</summary>
    [Fact]
    public void Count_a_blank_group_as_no_group()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(s_tonic, "   ", TimeSpan.FromSeconds(30), s_now);

        Assert.Equal(TimeSpan.FromSeconds(30), cooldowns.Remaining(s_tonic, "   ", s_now));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(s_elixir, "   ", s_now));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(s_elixir, "", s_now));
    }

    [Fact]
    public void Start_nothing_for_no_duration_or_a_blank_group()
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(s_tonic, "  ", TimeSpan.Zero, s_now);
        cooldowns.Start(s_elixir, "", TimeSpan.FromSeconds(5), s_now);

        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(s_tonic, null, s_now));
        Assert.Equal(TimeSpan.Zero, cooldowns.Remaining(s_tonic, "", s_now));
    }

    [Fact]
    public void Live_on_the_character_and_start_empty_with_every_new_session()
    {
        CharacterEntity character = Inventory.TestCharacters.New(7);
        character.ItemCooldowns.Start(s_tonic, null, TimeSpan.FromSeconds(30), s_now);

        Assert.Equal(TimeSpan.Zero, Inventory.TestCharacters.New(7).ItemCooldowns.Remaining(s_tonic, null, s_now));
    }
}
