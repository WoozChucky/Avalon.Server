using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.Reload;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging.Abstractions;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>
/// The catalog validates vendor stock on load (spec #432): a bad row is left out with an error
/// naming it, and every other row still loads. It also covers the Vendors reload area, which
/// builds the catalog.
/// </summary>
public class VendorCatalogShould
{
    private static VendorStock Row(int id, uint sequence, ItemTemplate item, CreatureTemplateId? vendor = null) =>
        new() { Id = id, CreatureTemplateId = vendor ?? Smith, Sequence = sequence, ItemTemplateId = item.Id };

    private static VendorStock Ghost(int id, uint sequence) =>
        new() { Id = id, CreatureTemplateId = Smith, Sequence = sequence, ItemTemplateId = new ItemTemplateId(9999) };

    private static VendorCatalog Load(params VendorStock[] rows) => new(rows, Items, NullLoggerFactory.Instance);

    [Fact]
    public void Order_each_vendors_rows_by_sequence()
    {
        VendorCatalog catalog = Load(Row(3, 30, Elixir), Row(1, 10, Tonic), Row(2, 20, Blade), Row(4, 5, Tonic, Pedlar));

        Assert.Equal([10u, 20u, 30u], catalog.RowsFor(Smith).Select(r => r.Sequence));
        Assert.Equal([5u], catalog.RowsFor(Pedlar).Select(r => r.Sequence));
        Assert.Equal((2, 4), (catalog.VendorCount, catalog.RowCount));
        Assert.Empty(catalog.Refused);
    }

    [Fact]
    public void Carry_the_limit_price_quest_and_costs_into_the_view()
    {
        VendorCatalog catalog = Catalog();
        IReadOnlyList<VendorStockView> rows = catalog.RowsFor(Smith);

        VendorStockView elixir = rows.Single(r => r.Sequence == ElixirSequence);
        Assert.True(elixir.IsLimited);
        Assert.Equal((5u, 600u), (elixir.MaxStock!.Value, elixir.RestockSeconds!.Value));
        Assert.Equal(new VendorCostView(Tonic.Id, 2), Assert.Single(elixir.Costs));

        VendorStockView charm = rows.Single(r => r.Sequence == CharmSequence);
        Assert.False(charm.IsLimited);
        Assert.Equal(40u, charm.PriceOverride);
        Assert.Null(charm.Requirement);

        VendorStockView gated = rows.Single(r => r.Sequence == GatedSequence);
        Assert.Equal(new QuestRequirement(GatedQuest, QuestRequirementState.Completed), gated.Requirement);
    }

    [Fact]
    public void Answer_no_rows_for_a_template_that_sells_nothing_and_one_list_per_vendor()
    {
        VendorCatalog catalog = Catalog();

        Assert.False(catalog.TryGet(new CreatureTemplateId(3), out _));
        Assert.Same(VendorCatalog.NoRows, catalog.RowsFor(new CreatureTemplateId(3)));

        // VendorStockState tells a reload from a repeat by comparing the list reference.
        Assert.True(catalog.TryGet(Smith, out IReadOnlyList<VendorStockView>? rows));
        Assert.Same(rows, catalog.RowsFor(Smith));
    }

    [Fact]
    public void Refuse_a_row_whose_item_template_is_missing_and_keep_the_rest()
    {
        VendorCatalog catalog = Load(Row(1, 1, Tonic), Ghost(2, 2), Row(3, 3, Blade));

        Assert.Equal([1u, 3u], catalog.RowsFor(Smith).Select(r => r.Sequence));
        VendorStockRefusal refusal = Assert.Single(catalog.Refused);
        Assert.Equal((2, Smith), (refusal.Id, refusal.Vendor));
        Assert.Contains("9999", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_a_limit_without_a_timer_and_a_timer_without_a_limit()
    {
        VendorStock limitOnly = Row(1, 1, Blade);
        limitOnly.MaxStock = 2;
        VendorStock timerOnly = Row(2, 2, Tonic);
        timerOnly.RestockSeconds = 60;

        VendorCatalog catalog = Load(limitOnly, timerOnly);

        Assert.Empty(catalog.RowsFor(Smith));
        Assert.Equal([1, 2], catalog.Refused.Select(r => r.Id));
    }

    [Fact]
    public void Refuse_a_limit_or_a_timer_of_zero()
    {
        VendorStock noStock = Row(1, 1, Blade);
        noStock.MaxStock = 0;
        noStock.RestockSeconds = 60;
        VendorStock noTime = Row(2, 2, Blade);
        noTime.MaxStock = 2;
        noTime.RestockSeconds = 0;

        VendorCatalog catalog = Load(noStock, noTime);

        Assert.Empty(catalog.RowsFor(Smith));
        Assert.Equal([1, 2], catalog.Refused.Select(r => r.Id));
    }

    [Fact]
    public void Refuse_half_a_quest_requirement()
    {
        VendorStock idOnly = Row(1, 1, Tonic);
        idOnly.RequiredQuestId = 7;
        VendorStock stateOnly = Row(2, 2, Tonic);
        stateOnly.RequiredQuestState = QuestRequirementState.Active;

        VendorCatalog catalog = Load(idOnly, stateOnly);

        Assert.Empty(catalog.RowsFor(Smith));
        Assert.Equal([1, 2], catalog.Refused.Select(r => r.Id));
    }

    [Fact]
    public void Refuse_a_cost_on_a_missing_template()
    {
        VendorStock row = Row(1, 1, Elixir);
        row.Costs = [new VendorStockCost { VendorStockId = 1, ItemTemplateId = new ItemTemplateId(9999), Count = 1 }];

        VendorCatalog catalog = Load(row);

        Assert.Empty(catalog.RowsFor(Smith));
        Assert.Contains("9999", Assert.Single(catalog.Refused).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_a_cost_count_of_zero_and_a_cost_named_twice()
    {
        VendorStock zero = Row(1, 1, Elixir);
        zero.Costs = [new VendorStockCost { VendorStockId = 1, ItemTemplateId = Tonic.Id, Count = 0 }];
        VendorStock twice = Row(2, 2, Elixir);
        twice.Costs =
        [
            new VendorStockCost { VendorStockId = 2, ItemTemplateId = Tonic.Id, Count = 1 },
            new VendorStockCost { VendorStockId = 2, ItemTemplateId = Tonic.Id, Count = 2 },
        ];

        VendorCatalog catalog = Load(zero, twice);

        Assert.Empty(catalog.RowsFor(Smith));
        Assert.Equal([1, 2], catalog.Refused.Select(r => r.Id));
    }

    [Fact]
    public void Refuse_a_duplicate_sequence_and_keep_the_lowest_id()
    {
        VendorCatalog catalog = Load(Row(7, 1, Blade), Row(3, 1, Tonic), Row(4, 1, Tonic, Pedlar));

        Assert.Equal(3, Assert.Single(catalog.RowsFor(Smith)).Id);
        Assert.Equal(7, Assert.Single(catalog.Refused).Id);
        // The same sequence at another vendor is a different row.
        Assert.Single(catalog.RowsFor(Pedlar));
    }

    [Fact]
    public void Describe_the_counts_and_every_refusal()
    {
        Assert.Equal("2 vendors, 6 rows", Catalog().Describe());

        string described = Load(Row(1, 1, Tonic), Ghost(2, 2)).Describe();

        Assert.StartsWith("1 vendors, 1 rows, 1 refused (stock row 2 of creature template 12: ", described, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_the_vendor_area_at_startup_and_replace_it_on_reload()
    {
        List<VendorStock> rows = Rows();
        StaticData data = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            items: () => Items, vendors: VendorRepositories.Of(() => rows)));

        Assert.Equal(5, data.Vendors.RowsFor(Smith).Count);

        rows.RemoveAll(r => r.Id == 4);
        StaticDataPatch patch = await data.PrepareAsync(ReloadArea.Vendors);
        Assert.Equal(5, data.Vendors.RowsFor(Smith).Count);   // prepared, not yet applied

        data.Apply(patch);

        Assert.Equal(4, data.Vendors.RowsFor(Smith).Count);
        Assert.Equal("2 vendors, 5 rows", patch.Describe());
    }

    [Fact]
    public async Task Load_an_empty_catalog_when_no_vendor_repository_is_given()
    {
        StaticData data = await TestStaticData.LoadAsync(items: Items);

        Assert.Equal(0, data.Vendors.VendorCount);
        Assert.Empty(data.Vendors.RowsFor(Smith));
    }

    /// <summary>A vendors reload reads the item templates itself, so it never pairs new rows with an older item list.</summary>
    [Fact]
    public async Task Validate_a_vendor_reload_against_the_items_it_reads_itself()
    {
        List<ItemTemplate> items = [.. Items];
        StaticData data = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            items: () => items, vendors: VendorRepositories.Of(Rows)));
        items.RemoveAll(i => i.Id == Tonic.Id);

        data.Apply(await data.PrepareAsync(ReloadArea.Vendors));

        // Row 1 sells Tonic, row 3 costs Tonic, and row 10 is the Pedlar's Tonic.
        Assert.Equal([2, 4, 5], data.Vendors.RowsFor(Smith).Select(r => r.Id));
        Assert.Equal([1, 3, 10], data.Vendors.Refused.Select(r => r.Id));
    }
}
