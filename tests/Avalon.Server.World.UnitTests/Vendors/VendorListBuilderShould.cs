using Avalon.Domain.World;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Quests;
using Avalon.World.Reload;
using Avalon.World.Vendors;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>What SMSG_VENDOR_LIST tells a player (spec #432), and when the vendor pass sends it.</summary>
public class VendorListBuilderShould : IAsyncLifetime
{
    private VendorWorld _w = null!;

    public async Task InitializeAsync() => _w = await VendorWorld.CreateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private VendorStockState SmithStock() =>
        _w.Stocks.For(VendorWorld.SmithGuid, Smith, _w.Data.Vendors.RowsFor(Smith));

    private SVendorListPacket Build(IQuestProgress? quests = null) =>
        VendorListBuilder.Build(VendorWorld.SmithGuid, SmithStock(), _w.Main.Character, _w.Data, quests ?? NoQuestProgress.Instance);

    [Fact]
    public void List_every_visible_row_in_sequence_with_its_price_stock_and_costs()
    {
        VendorStockState stock = SmithStock();
        stock.Take(stock.Rows.Single(r => r.Sequence == BladeSequence), 1, Now);

        SVendorListPacket list = Build();

        Assert.Equal(VendorWorld.SmithGuid.RawValue, list.VendorGuid);
        Assert.Equal([1u, 2u, 3u, 4u], list.Entries.Select(e => e.Sequence));
        VendorEntryDto tonic = list.Entries[0], blade = list.Entries[1], elixir = list.Entries[2], charm = list.Entries[3];
        Assert.Equal((Tonic.Id.Value, 10UL, (uint?)null), (tonic.ItemTemplateId, tonic.Price, tonic.Stock));
        Assert.Equal((100UL, (uint?)1), (blade.Price, blade.Stock));
        Assert.Equal((30UL, (uint?)5), (elixir.Price, elixir.Stock));
        VendorCostDto cost = Assert.Single(elixir.Costs);
        Assert.Equal((Tonic.Id.Value, 2u), (cost.ItemTemplateId, cost.Count));
        Assert.Equal(40UL, charm.Price);   // the override, not the Charm's BuyPrice of 50
        Assert.Empty(charm.Costs);
    }

    [Fact]
    public void List_a_gated_row_once_its_quest_is_met()
    {
        var quests = Substitute.For<IQuestProgress>();
        quests.IsMet(_w.Main.Character, GatedQuest, QuestRequirementState.Completed).Returns(true);

        SVendorListPacket list = Build(quests);

        VendorEntryDto plate = list.Entries.Single(e => e.Sequence == GatedSequence);
        Assert.Equal((Plate.Id.Value, 80UL), (plate.ItemTemplateId, plate.Price));
    }

    /// <summary>A /reload items that drops a stocked template leaves its row unsellable, so it is not listed.</summary>
    [Fact]
    public void Leave_out_a_row_whose_item_template_is_gone()
    {
        _w.Data.Apply(new ItemsPatch(Items.Where(i => i.Id != Tonic.Id).ToList()));

        SVendorListPacket list = Build();

        Assert.Equal([2u, 3u, 4u], list.Entries.Select(e => e.Sequence));
    }

    [Fact]
    public void List_the_buyback_newest_first_with_what_each_sold_for()
    {
        CharacterEntity character = _w.Main.Character;
        character.Buyback.Push(new BuybackEntry(TestCharacters.Item(3, Tonic, count: 5), 20));
        character.Buyback.Push(new BuybackEntry(TestCharacters.Item(4, Blade, durability: 42), 25));

        SVendorListPacket list = Build();

        Assert.Equal([0u, 1u], list.Buyback.Select(b => b.Index));
        Assert.Equal((Blade.Id.Value, (ushort)4, 42u, 25UL),
            (list.Buyback[0].Item!.ItemTemplateId, list.Buyback[0].Item!.Slot, list.Buyback[0].Item!.Durability, list.Buyback[0].Price));
        Assert.Equal((5u, 20UL), (list.Buyback[1].Item!.Count, list.Buyback[1].Price));
        Assert.Equal((ushort)1, list.Buyback[1].Item!.Container);   // the Bag
    }

    [Fact]
    public void Send_the_list_when_the_stock_changed_and_nothing_when_it_did_not()
    {
        _w.OpenShop();
        _w.EndOfTick();
        Assert.Single(_w.Main.Lists());   // the opening only

        VendorStockState stock = SmithStock();
        stock.Take(stock.Rows.Single(r => r.Sequence == BladeSequence), 1, Now);
        _w.EndOfTick();
        _w.EndOfTick();

        Assert.Equal(2, _w.Main.Lists().Count);
        Assert.Equal((uint?)1, _w.Main.Lists()[^1].Entries.Single(e => e.Sequence == BladeSequence).Stock);
    }

    [Fact]
    public void Send_the_list_when_the_buyback_changed_and_forget_it_once_sent()
    {
        _w.OpenShop();
        _w.Main.Character.Buyback.Push(new BuybackEntry(TestCharacters.Item(3, Tonic), 4));
        _w.Main.Character.VendorListOwed = true;

        _w.EndOfTick();
        _w.EndOfTick();

        Assert.Equal(2, _w.Main.Lists().Count);
        Assert.Single(_w.Main.Lists()[^1].Buyback);
        Assert.False(_w.Main.Character.VendorListOwed);
    }

    [Fact]
    public void Send_nothing_to_a_shopper_whose_shop_closed_and_forget_what_it_was_owed()
    {
        _w.OpenShop();
        _w.Main.Connection.CurrentDialogue = null;   // the conversation ended some other way
        _w.Main.Character.VendorListOwed = true;
        VendorStockState stock = SmithStock();
        stock.Take(stock.Rows.Single(r => r.Sequence == BladeSequence), 1, Now);

        _w.EndOfTick();

        Assert.Single(_w.Main.Lists());
        Assert.False(_w.Main.Character.VendorListOwed);
    }
}
