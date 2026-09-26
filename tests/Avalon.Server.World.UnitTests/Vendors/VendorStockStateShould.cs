using Avalon.Domain.World;
using Avalon.World.Vendors;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>
/// One vendor's live stock (spec #432): counts per limited row, a shared restock timer per row,
/// and counts carried over a reload by row id.
/// </summary>
public class VendorStockStateShould
{
    private static VendorStockView RowOf(VendorStockState state, uint sequence) =>
        state.Rows.Single(r => r.Sequence == sequence);

    [Fact]
    public void Start_every_limited_row_full_and_count_no_unlimited_row()
    {
        VendorStockState state = Stock();

        Assert.Equal(2u, state.Available(RowOf(state, BladeSequence)));
        Assert.Equal(5u, state.Available(RowOf(state, ElixirSequence)));
        Assert.Null(state.Available(RowOf(state, TonicSequence)));
        Assert.False(state.Changed);
    }

    [Fact]
    public void Take_from_a_limited_row_and_mark_the_list_changed()
    {
        VendorStockState state = Stock();

        state.Take(RowOf(state, BladeSequence), 1, Now);

        Assert.Equal(1u, state.Available(RowOf(state, BladeSequence)));
        Assert.True(state.Changed);
    }

    [Fact]
    public void Count_nothing_and_change_nothing_for_an_unlimited_row()
    {
        VendorStockState state = Stock();

        state.Take(RowOf(state, TonicSequence), 7, Now);

        Assert.Null(state.Available(RowOf(state, TonicSequence)));
        Assert.False(state.Changed);
    }

    [Fact]
    public void Refuse_to_take_more_than_is_left()
    {
        VendorStockState state = Stock();

        Assert.Throws<InvalidOperationException>(() => state.Take(RowOf(state, BladeSequence), 3, Now));
        Assert.Equal(2u, state.Available(RowOf(state, BladeSequence)));
    }

    [Fact]
    public void Refill_only_once_the_restock_is_due()
    {
        VendorStockState state = Stock();
        state.Take(RowOf(state, BladeSequence), 2, Now);
        state.ClearChanged();

        state.Restock(Now.AddSeconds(59));
        Assert.Equal(0u, state.Available(RowOf(state, BladeSequence)));
        Assert.False(state.Changed);

        state.Restock(Now.AddSeconds(60));
        Assert.Equal(2u, state.Available(RowOf(state, BladeSequence)));
        Assert.True(state.Changed);
    }

    [Fact]
    public void Start_the_timer_at_the_first_sale_and_never_push_it_back()
    {
        VendorStockState state = Stock();
        state.Take(RowOf(state, BladeSequence), 1, Now);
        state.Take(RowOf(state, BladeSequence), 1, Now.AddSeconds(30));

        state.Restock(Now.AddSeconds(60));

        Assert.Equal(2u, state.Available(RowOf(state, BladeSequence)));
    }

    [Fact]
    public void Run_no_timer_after_a_refill_until_the_next_sale()
    {
        VendorStockState state = Stock();
        state.Take(RowOf(state, BladeSequence), 1, Now);
        state.Restock(Now.AddSeconds(60));
        state.ClearChanged();

        state.Restock(Now.AddSeconds(500));

        Assert.False(state.Changed);
        Assert.Equal(2u, state.Available(RowOf(state, BladeSequence)));
    }

    /// <summary>A lowered limit clamps, a running timer survives, a new row starts full, a removed row is dropped.</summary>
    [Fact]
    public void Carry_counts_over_a_reload_by_row_id()
    {
        VendorStockState state = Stock();
        state.Take(RowOf(state, ElixirSequence), 1, Now);   // 4 left, restock due at Now + 600
        state.ClearChanged();

        List<VendorStock> rows = Rows();
        rows.Single(r => r.Id == 2).MaxStock = 1;           // Blade: 2 left, now at most 1
        rows.Add(new VendorStock
        {
            Id = 6, CreatureTemplateId = Smith, Sequence = 6, ItemTemplateId = Tonic.Id, MaxStock = 3, RestockSeconds = 60,
        });
        state.Reconcile(Catalog(rows).RowsFor(Smith));

        Assert.True(state.Changed);
        Assert.Equal(1u, state.Available(RowOf(state, BladeSequence)));
        Assert.Equal(4u, state.Available(RowOf(state, ElixirSequence)));
        Assert.Equal(3u, state.Available(RowOf(state, 6)));
        state.Restock(Now.AddSeconds(600));
        Assert.Equal(5u, state.Available(RowOf(state, ElixirSequence)));

        // Drop the Elixir row, then bring it back: it returns full, so nothing was kept for it.
        state.Take(RowOf(state, ElixirSequence), 2, Now.AddSeconds(601));
        state.Reconcile(Catalog(Rows().Where(r => r.Id != 3).ToList()).RowsFor(Smith));
        Assert.DoesNotContain(state.Rows, r => r.Id == 3);
        state.Reconcile(Catalog().RowsFor(Smith));
        Assert.Equal(5u, state.Available(RowOf(state, ElixirSequence)));
    }

    /// <summary>
    /// A reload that raises MaxStock leaves a full row below its new maximum with no sale to start
    /// its timer, so the reload starts it, timed from the next pass.
    /// </summary>
    [Fact]
    public void Start_the_timer_when_a_reload_raises_the_maximum_above_the_count()
    {
        VendorStockState state = Stock();                   // Blade: 2 of 2, no timer running

        List<VendorStock> rows = Rows();
        rows.Single(r => r.Id == 2).MaxStock = 3;           // Blade: 2 left, now at most 3
        state.Reconcile(Catalog(rows).RowsFor(Smith));
        Assert.Equal(2u, state.Available(RowOf(state, BladeSequence)));

        state.Restock(Now);                                 // the first pass after the reload
        state.ClearChanged();

        state.Restock(Now.AddSeconds(59));
        Assert.Equal(2u, state.Available(RowOf(state, BladeSequence)));
        Assert.False(state.Changed);

        state.Restock(Now.AddSeconds(60));
        Assert.Equal(3u, state.Available(RowOf(state, BladeSequence)));
        Assert.True(state.Changed);
    }

    [Fact]
    public void Change_nothing_when_reconciled_with_the_rows_it_already_has()
    {
        VendorCatalog catalog = Catalog();
        VendorStockState state = Stock(catalog);
        state.Take(RowOf(state, BladeSequence), 1, Now);
        state.ClearChanged();

        state.Reconcile(catalog.RowsFor(Smith));

        Assert.False(state.Changed);
        Assert.Equal(1u, state.Available(RowOf(state, BladeSequence)));
    }
}
