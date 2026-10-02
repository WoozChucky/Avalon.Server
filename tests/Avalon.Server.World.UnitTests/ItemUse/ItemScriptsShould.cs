using Avalon.Network.Packets.State;
using Avalon.World.Items;
using Avalon.World.Items.Scripts;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>The first item scripts, against a substitute context.</summary>
public class ItemScriptsShould
{
    private readonly IItemUseContext _ctx = Substitute.For<IItemUseContext>();

    [Fact]
    public void Restore_a_percentage_of_the_maximum_health_and_consume_one()
    {
        _ctx.MaxHealth.Returns(240u);
        _ctx.Health.Returns(100u);
        _ctx.UseValue.Returns(30u);

        Assert.Null(new RestoreHealth().CanUse(_ctx));
        new RestoreHealth().OnUse(_ctx);

        _ctx.Received(1).RestoreHealth(72u);
        _ctx.Received(1).Consume(1u);
    }

    [Fact]
    public void Refuse_a_health_potion_at_full_health()
    {
        _ctx.MaxHealth.Returns(240u);
        _ctx.Health.Returns(240u);

        Assert.Equal("You are already at full health.", new RestoreHealth().CanUse(_ctx));
    }

    [Theory]
    [InlineData(PowerType.Mana)]
    [InlineData(PowerType.Energy)]
    public void Restore_a_percentage_of_mana_or_energy_and_consume_one(PowerType pool)
    {
        _ctx.PowerType.Returns(pool);
        _ctx.MaxPower.Returns(200u);
        _ctx.Power.Returns(10u);
        _ctx.UseValue.Returns(30u);

        Assert.Null(new RestorePower().CanUse(_ctx));
        new RestorePower().OnUse(_ctx);

        _ctx.Received(1).RestorePower(60u);
        _ctx.Received(1).Consume(1u);
    }

    [Theory]
    [InlineData(PowerType.Fury)]
    [InlineData(PowerType.None)]
    public void Refuse_a_mana_potion_to_a_pool_it_cannot_fill(PowerType pool)
    {
        _ctx.PowerType.Returns(pool);

        Assert.Equal("You cannot drink this.", new RestorePower().CanUse(_ctx));
    }

    [Fact]
    public void Refuse_a_mana_potion_at_full_power()
    {
        _ctx.PowerType.Returns(PowerType.Mana);
        _ctx.MaxPower.Returns(200u);
        _ctx.Power.Returns(200u);

        Assert.Equal("Your mana is already full.", new RestorePower().CanUse(_ctx));
    }

    [Fact]
    public void Refuse_a_town_portal_in_a_town_or_while_a_return_is_under_way()
    {
        _ctx.InTown.Returns(true);
        Assert.Equal("You are already in town.", new TownPortalScroll().CanUse(_ctx));

        _ctx.InTown.Returns(false);
        _ctx.ReturningToTown.Returns(true);
        Assert.Equal("You are already on your way to town.", new TownPortalScroll().CanUse(_ctx));
    }

    [Fact]
    public void Return_to_town_and_consume_the_scroll_only_when_the_return_started()
    {
        _ctx.ReturnToTown().Returns(true);
        new TownPortalScroll().OnUse(_ctx);
        _ctx.Received(1).Consume(1u);

        _ctx.ClearReceivedCalls();
        _ctx.ReturnToTown().Returns(false);
        new TownPortalScroll().OnUse(_ctx);
        _ctx.DidNotReceiveWithAnyArgs().Consume(default);
    }
}
