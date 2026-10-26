using Avalon.World.Items;
using Avalon.World.Items.Scripts;
using NSubstitute;

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

    /// <summary>
    /// #1181: every class drinks the Mana Potion; only a full pool (a maximum of 0 included) refuses it. The use is run as
    /// the item-use pipeline runs it: OnUse only once CanUse found nothing to refuse.
    /// </summary>
    [Theory]
    [InlineData(40u, 200u, null, 60u)]
    [InlineData(200u, 200u, "Your mana is already full.", 0u)]
    [InlineData(0u, 0u, "Your mana is already full.", 0u)]
    public void Restore_a_percentage_of_mana_and_refuse_a_full_pool(uint power, uint max, string? refusal, uint restored)
    {
        _ctx.MaxPower.Returns(max);
        _ctx.Power.Returns(power);
        _ctx.UseValue.Returns(30u);

        string? answer = new RestorePower().CanUse(_ctx);
        if (answer is null)
            new RestorePower().OnUse(_ctx);

        Assert.Equal(refusal, answer);
        if (refusal is null)
        {
            _ctx.Received(1).RestorePower(restored);
            _ctx.Received(1).Consume(1u);
        }
        else
        {
            _ctx.DidNotReceiveWithAnyArgs().RestorePower(default);
            _ctx.DidNotReceiveWithAnyArgs().Consume(default);
        }
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
