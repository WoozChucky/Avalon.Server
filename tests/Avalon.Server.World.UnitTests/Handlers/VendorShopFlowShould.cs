using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Vendors;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// The shop end to end (spec #432). It is opened through the vendor's dialogue, used by the three
/// requests, closed by the leash and by talking to someone else, and its list reaches everyone
/// with it open. EndOfTick is the instance's vendor pass, and InventoryUpdateFlusher.Flush the
/// tick's inventory flush.
/// </summary>
public class VendorShopFlowShould : IAsyncLifetime
{
    private VendorWorld _w = null!;

    public async Task InitializeAsync() => _w = await VendorWorld.CreateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private CharacterEntity Me => _w.Main.Character;

    private SInventoryUpdatePacket Flush()
    {
        InventoryUpdateFlusher.Flush(_w.Main.Connection);
        return _w.Main.Updates()[^1];
    }

    [Fact]
    public void Open_buy_sell_and_buy_back_at_the_vendor()
    {
        _w.OpenShop();

        // Buy: the gold and the item leave in the tick's inventory update, the new count in the list.
        Assert.Equal(VendorResult.Ok, _w.Buy(_w.Main, 1, BladeSequence));
        SInventoryUpdatePacket bought = Flush();
        Assert.Equal(900UL, bought.Money);
        ItemSlotDto blade = bought.Slots.Single(s => s.Container == (ushort)InventoryType.Bag && s.Slot == 0).Item!;
        Assert.Equal((Blade.Id.Value, 42u), (blade.ItemTemplateId, blade.Durability));
        _w.EndOfTick();
        Assert.Equal((uint?)1, _w.Main.Lists()[^1].Entries.Single(e => e.Sequence == BladeSequence).Stock);

        // Sell it back: the slot empties, and it heads the buyback list.
        Assert.Equal(VendorResult.Ok, _w.Sell(_w.Main, 2, 0));
        SInventoryUpdatePacket sold = Flush();
        Assert.Equal(925UL, sold.Money);
        Assert.Null(sold.Slots.Single(s => s.Slot == 0).Item);
        _w.EndOfTick();
        VendorBuybackDto entry = Assert.Single(_w.Main.Lists()[^1].Buyback);
        Assert.Equal((blade.ItemInstanceId, 25UL), (entry.Item!.ItemInstanceId, entry.Price));

        // Buy it back: the same instance returns, and the list empties.
        Assert.Equal(VendorResult.Ok, _w.Buyback(_w.Main, 3, 0));
        SInventoryUpdatePacket back = Flush();
        Assert.Equal(900UL, back.Money);
        Assert.Equal(blade.ItemInstanceId, back.Slots.Single(s => s.Slot == 0).Item!.ItemInstanceId);
        _w.EndOfTick();
        Assert.Empty(_w.Main.Lists()[^1].Buyback);

        // The opening, then one list per change.
        Assert.Equal(4, _w.Main.Lists().Count);
    }

    [Fact]
    public void Buy_an_item_that_costs_other_items()
    {
        _w.OpenShop();

        Assert.Equal(VendorResult.Ok, _w.Buy(_w.Main, 1, TonicSequence, 2));
        Assert.Equal(VendorResult.Ok, _w.Buy(_w.Main, 2, ElixirSequence));

        Assert.Equal(1000UL - 20 - 30, Me.Data!.Money);
        InventoryItem only = Assert.Single(Me.Container(InventoryType.Bag).Items);
        Assert.Equal((Elixir.Id, 1u), (only.TemplateId, only.Count));
    }

    [Fact]
    public void Close_the_shop_on_the_leash_and_refuse_it_after()
    {
        _w.OpenShop();
        Me.Position = new Vector3(0, 0, 25);

        Assert.Equal(VendorResult.ShopClosed, _w.Buy(_w.Main, 1, TonicSequence));
        Assert.Single(_w.Main.Ends());

        // Walking back does not reopen it: that takes the dialogue again.
        Me.Position = Vector3.zero;
        Assert.Equal(VendorResult.ShopClosed, _w.Buy(_w.Main, 2, TonicSequence));
        Assert.Single(_w.Main.Ends());
        Assert.Equal(1000UL, Me.Data!.Money);
    }

    [Fact]
    public void Close_the_shop_when_the_player_switches_to_another_vendor()
    {
        _w.OpenShop();

        _w.Interact(_w.Main, VendorWorld.PedlarGuid);
        Assert.Equal(VendorResult.ShopClosed, _w.Buy(_w.Main, 1, TonicSequence));

        _w.Choose(_w.Main, VendorWorld.PedlarGuid, VendorWorld.PedlarRoot, VendorWorld.PedlarWares);
        Assert.Equal(VendorResult.Ok, _w.Buy(_w.Main, 2, 1));   // the Pedlar's own row 1
        Assert.Equal(VendorWorld.PedlarGuid.RawValue, _w.Main.Lists()[^1].VendorGuid);
    }

    [Fact]
    public void Reach_everyone_with_the_shop_open_when_it_restocks()
    {
        VendorWorld.Shopper other = _w.AddShopper(8, money: 1000);
        _w.OpenShop();
        _w.OpenShop(other);

        Assert.Equal(VendorResult.Ok, _w.Buy(_w.Main, 1, BladeSequence));
        Assert.Equal(VendorResult.Ok, _w.Buy(other, 1, BladeSequence));
        _w.EndOfTick();

        foreach (VendorWorld.Shopper shopper in new[] { _w.Main, other })
            Assert.Equal((uint?)0, shopper.Lists()[^1].Entries.Single(e => e.Sequence == BladeSequence).Stock);

        _w.Clock.Now = _w.Clock.Now.AddSeconds(60);
        _w.EndOfTick();

        foreach (VendorWorld.Shopper shopper in new[] { _w.Main, other })
        {
            Assert.Equal(3, shopper.Lists().Count);
            Assert.Equal((uint?)2, shopper.Lists()[^1].Entries.Single(e => e.Sequence == BladeSequence).Stock);
        }
    }

    [Fact]
    public void Keep_each_buyback_list_to_the_player_who_sold()
    {
        VendorWorld.Shopper other = _w.AddShopper(8);
        _w.OpenShop();
        _w.OpenShop(other);
        Me.Container(InventoryType.Bag).Load([TestCharacters.Item(0, Tonic, count: 5)]);

        Assert.Equal(VendorResult.Ok, _w.Sell(_w.Main, 1, 0));
        _w.EndOfTick();

        Assert.Single(_w.Main.Lists()[^1].Buyback);
        Assert.Single(other.Lists());   // only its opening: nothing about it changed
    }
}
