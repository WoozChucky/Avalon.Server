using Avalon.Common.ValueObjects;
using Avalon.World.Items;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>Item use cooldowns: per item and per group, in memory, measured on absolute time.</summary>
public class ItemCooldownsShould
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly ItemTemplateId s_tonic = new(700);
    private static readonly ItemTemplateId s_elixir = new(701);

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

    /// <summary>An item off its own cooldown still waits for its group, and the other way round.</summary>
    [Theory]
    [InlineData(60, 10)]
    [InlineData(10, 60)]
    public void Answer_the_longer_of_the_item_and_its_group(int itemSeconds, int groupSeconds)
    {
        var cooldowns = new ItemCooldowns();
        cooldowns.Start(s_tonic, null, TimeSpan.FromSeconds(itemSeconds), s_now);
        cooldowns.Start(s_elixir, "potion", TimeSpan.FromSeconds(groupSeconds), s_now);

        Assert.Equal(TimeSpan.FromSeconds(Math.Max(itemSeconds, groupSeconds)), cooldowns.Remaining(s_tonic, "potion", s_now));
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
}
