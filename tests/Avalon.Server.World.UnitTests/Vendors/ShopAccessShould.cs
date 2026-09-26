using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Vendors;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>
/// When a shop may be used (spec #432): only in an open conversation with a live vendor in which
/// its OpenShop option was chosen, and inside the dialogue leash. When anything else fails, the
/// conversation is ended out loud.
/// </summary>
public class ShopAccessShould : IAsyncLifetime
{
    private VendorWorld _w = null!;

    public async Task InitializeAsync() => _w = await VendorWorld.CreateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private bool TryUse() => ShopAccess.TryUse(_w.Main.Connection, _w.Main.Character, _w.World, out _);

    private void AssertEndedWithTheSmith()
    {
        Assert.Null(_w.Main.Connection.CurrentDialogue);
        Assert.Null(_w.Main.Character.OpenShopNpc);
        Assert.Equal(VendorWorld.SmithGuid.RawValue, Assert.Single(_w.Main.Ends()).SpeakerGuid);
    }

    [Fact]
    public void Stay_closed_until_the_shop_option_is_chosen()
    {
        _w.Interact(_w.Main, VendorWorld.SmithGuid);

        Assert.False(ShopAccess.IsOpen(_w.Main.Connection, _w.Main.Character));
        Assert.False(TryUse());
        Assert.Empty(_w.Main.Ends());   // nothing was open, so nothing is ended
    }

    [Fact]
    public void Open_while_the_conversation_with_that_vendor_lasts()
    {
        _w.OpenShop();

        Assert.True(ShopAccess.IsOpen(_w.Main.Connection, _w.Main.Character));
        Assert.True(ShopAccess.TryUse(_w.Main.Connection, _w.Main.Character, _w.World, out VendorStockState? stock));
        Assert.True(_w.Stocks.TryGet(VendorWorld.SmithGuid, out VendorStockState? held));
        Assert.Same(held, stock);
    }

    [Fact]
    public void Close_when_the_conversation_moves_to_another_npc()
    {
        _w.OpenShop();

        _w.Main.Connection.CurrentDialogue = (VendorWorld.PedlarGuid, new DialogueNodeId(VendorWorld.PedlarRoot));

        Assert.False(ShopAccess.IsOpen(_w.Main.Connection, _w.Main.Character));
    }

    [Fact]
    public void Refuse_and_end_the_conversation_past_the_leash()
    {
        _w.OpenShop();
        _w.Main.Character.Position = new Vector3(0, 0, 25);

        Assert.False(TryUse());

        AssertEndedWithTheSmith();
    }

    [Fact]
    public void Refuse_and_end_the_conversation_when_the_vendor_is_dead()
    {
        _w.OpenShop();
        _w.Smith.CurrentHealth.Returns(0u);

        Assert.False(TryUse());

        AssertEndedWithTheSmith();
    }

    [Fact]
    public void Refuse_and_end_the_conversation_with_an_npc_that_sells_nothing()
    {
        // As a /reload dialogue that took the Smith's OpenShop option away would leave it.
        _w.Interact(_w.Main, VendorWorld.InnkeeperGuid);
        _w.Main.Character.OpenShopNpc = VendorWorld.InnkeeperGuid;

        Assert.False(TryUse());

        Assert.Null(_w.Main.Connection.CurrentDialogue);
        Assert.Equal(VendorWorld.InnkeeperGuid.RawValue, Assert.Single(_w.Main.Ends()).SpeakerGuid);
    }

    /// <summary>The Smith is in the plain instance too, alive and in range: only the missing stock refuses.</summary>
    [Fact]
    public void Refuse_and_end_the_conversation_in_an_instance_that_keeps_no_stock()
    {
        _w.OpenShop();
        _w.Main.Character.InstanceId = VendorWorld.PlainInstanceId;

        Assert.False(TryUse());

        AssertEndedWithTheSmith();
    }
}
