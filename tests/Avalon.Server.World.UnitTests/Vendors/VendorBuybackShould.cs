using Avalon.World.Vendors;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>The last ten sales of a session, newest first (spec #432).</summary>
public class VendorBuybackShould
{
    private static BuybackEntry Sold(ulong price) => new(Item(0, Tonic), price);

    [Fact]
    public void Put_the_newest_sale_first()
    {
        var buyback = new VendorBuyback();

        buyback.Push(Sold(1));
        buyback.Push(Sold(2));

        Assert.Equal([2UL, 1UL], buyback.Entries.Select(e => e.Price));
    }

    [Fact]
    public void Push_the_oldest_sale_out_on_the_eleventh()
    {
        var buyback = new VendorBuyback();

        for (ulong price = 1; price <= 11; price++)
            buyback.Push(Sold(price));

        Assert.Equal(VendorBuyback.Capacity, buyback.Entries.Count);
        Assert.Equal(11UL, buyback.Entries[0].Price);
        Assert.Equal(2UL, buyback.Entries[^1].Price);   // the first sale is gone for good
    }

    [Fact]
    public void Find_no_entry_past_the_list()
    {
        var buyback = new VendorBuyback();
        buyback.Push(Sold(1));

        Assert.True(buyback.TryGet(0, out BuybackEntry? entry));
        Assert.Equal(1UL, entry.Price);
        Assert.False(buyback.TryGet(1, out _));
        Assert.False(buyback.TryGet(uint.MaxValue, out _));
    }

    [Fact]
    public void Remove_one_entry_and_close_the_gap()
    {
        var buyback = new VendorBuyback();
        buyback.Push(Sold(1));
        buyback.Push(Sold(2));
        buyback.Push(Sold(3));

        buyback.RemoveAt(1);

        Assert.Equal([3UL, 1UL], buyback.Entries.Select(e => e.Price));
    }

    [Fact]
    public void Forget_every_sale_on_clear()
    {
        var buyback = new VendorBuyback();
        buyback.Push(Sold(1));

        buyback.Clear();

        Assert.Empty(buyback.Entries);
    }
}
