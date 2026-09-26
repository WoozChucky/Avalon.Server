using Avalon.Common;
using Avalon.Domain.World;
using Avalon.World.Vendors;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>A town instance's vendor stock: one state per vendor creature, updated on the instance's tick.</summary>
public class VendorStocksShould
{
    private static readonly ObjectGuid SmithGuid = new(ObjectType.Creature, 92);
    private static readonly ObjectGuid PedlarGuid = new(ObjectType.Creature, 93);

    [Fact]
    public void Keep_one_state_per_vendor_creature()
    {
        VendorCatalog catalog = Catalog();
        var stocks = new VendorStocks();

        VendorStockState first = stocks.For(SmithGuid, Smith, catalog.RowsFor(Smith));
        VendorStockState again = stocks.For(SmithGuid, Smith, catalog.RowsFor(Smith));
        stocks.For(PedlarGuid, Pedlar, catalog.RowsFor(Pedlar));

        Assert.Same(first, again);
        Assert.Equal(2, stocks.Count);
        Assert.True(stocks.TryGet(SmithGuid, out VendorStockState? found));
        Assert.Same(first, found);
        Assert.False(stocks.TryGet(new ObjectGuid(ObjectType.Creature, 94), out _));
    }

    [Fact]
    public void Restock_every_vendor_whose_timer_is_due()
    {
        VendorCatalog catalog = Catalog();
        var stocks = new VendorStocks();
        VendorStockState smith = stocks.For(SmithGuid, Smith, catalog.RowsFor(Smith));
        VendorStockView blade = smith.Rows.Single(r => r.Sequence == BladeSequence);
        smith.Take(blade, 2, Now);

        stocks.Update(Now.AddSeconds(60), catalog, Items);

        Assert.Equal(2u, smith.Available(blade));
    }

    [Fact]
    public void Reconcile_every_vendor_against_the_current_catalog()
    {
        var stocks = new VendorStocks();
        VendorStockState smith = stocks.For(SmithGuid, Smith, Catalog().RowsFor(Smith));
        smith.ClearChanged();

        List<VendorStock> rows = Rows();
        rows.RemoveAll(r => r.Id == 4);
        VendorCatalog reloaded = Catalog(rows);
        stocks.Update(Now, reloaded, Items);

        Assert.True(smith.Changed);
        Assert.Same(reloaded.RowsFor(Smith), smith.Rows);
    }

    [Fact]
    public void Clear_every_change_mark()
    {
        VendorCatalog catalog = Catalog();
        var stocks = new VendorStocks();
        VendorStockState smith = stocks.For(SmithGuid, Smith, catalog.RowsFor(Smith));
        smith.Take(smith.Rows.Single(r => r.Sequence == BladeSequence), 1, Now);

        stocks.ClearChanged();

        Assert.False(smith.Changed);
    }

    /// <summary>The instance runs this every tick while anyone has shopped there, so it must not allocate.</summary>
    [Fact]
    public void Update_without_allocating_when_nothing_is_due()
    {
        VendorCatalog catalog = Catalog();
        IReadOnlyCollection<ItemTemplate> items = Items;   // one snapshot, as StaticData holds it between reloads
        var stocks = new VendorStocks();
        VendorStockState smith = stocks.For(SmithGuid, Smith, catalog.RowsFor(Smith));
        stocks.For(PedlarGuid, Pedlar, catalog.RowsFor(Pedlar));
        smith.Take(smith.Rows.Single(r => r.Sequence == BladeSequence), 1, Now);   // a timer is running, not due
        stocks.Update(Now, catalog, items);
        stocks.ClearChanged();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 100; tick++)
        {
            stocks.Update(Now.AddSeconds(1), catalog, items);
            stocks.ClearChanged();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.False(smith.Changed);
    }
}
