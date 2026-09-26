using System.Diagnostics.CodeAnalysis;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Vendors;

/// <summary>One item a stock row costs on top of its gold, per unit bought.</summary>
public sealed record VendorCostView(ItemTemplateId ItemTemplateId, uint Count);

/// <summary>The quest a stock row is hidden behind until IQuestProgress says it is met.</summary>
public sealed record QuestRequirement(uint QuestId, QuestRequirementState State);

/// <summary>One validated stock row. Costs are in item template id order.</summary>
public sealed record VendorStockView(
    int Id,
    uint Sequence,
    ItemTemplateId ItemTemplateId,
    uint? MaxStock,
    uint? RestockSeconds,
    uint? PriceOverride,
    QuestRequirement? Requirement,
    IReadOnlyList<VendorCostView> Costs)
{
    /// <summary>True when the row has a count that sells out and restocks.</summary>
    public bool IsLimited => MaxStock is not null;
}

/// <summary>A stock row left out of the catalog, and why.</summary>
public sealed record VendorStockRefusal(int Id, CreatureTemplateId Vendor, string Reason)
{
    public override string ToString() => $"stock row {Id} of creature template {Vendor.Value}: {Reason}";
}

/// <summary>
/// Every vendor stock row that passed validation, by vendor creature template, in Sequence order
/// (spec #432). Built off the tick thread by the Vendors reload area and immutable afterwards, so
/// the tick reads it without locking.
/// </summary>
/// <remarks>
/// A row that fails validation is left out with an error naming it, and every other row still
/// loads, so one bad row costs that row rather than a vendor, a reload, or the server's startup.
/// Rows are read in Id order, so of two rows claiming one vendor's Sequence the lower Id is kept.
/// Each vendor's list is one instance for the life of the catalog: VendorStockState compares it
/// by reference to tell a reload from a repeat.
/// </remarks>
public sealed class VendorCatalog
{
    public static readonly IReadOnlyList<VendorStockView> NoRows = [];

    private readonly Dictionary<ulong, IReadOnlyList<VendorStockView>> _byVendor;

    public VendorCatalog(
        IReadOnlyCollection<VendorStock> rows,
        IReadOnlyCollection<ItemTemplate> items,
        ILoggerFactory loggerFactory)
    {
        ILogger<VendorCatalog> logger = loggerFactory.CreateLogger<VendorCatalog>();
        HashSet<ulong> known = items.Select(i => i.Id.Value).ToHashSet();
        HashSet<(ulong Vendor, uint Sequence)> taken = [];
        List<VendorStock> accepted = [];
        List<VendorStockRefusal> refused = [];

        foreach (VendorStock row in rows.OrderBy(r => r.Id))
        {
            string? reason = Problem(row, known);
            if (reason is null && !taken.Add((row.CreatureTemplateId.Value, row.Sequence)))
                reason = $"sequence {row.Sequence} is already used by another row of this vendor";

            if (reason is not null)
            {
                refused.Add(new VendorStockRefusal(row.Id, row.CreatureTemplateId, reason));
                continue;
            }

            accepted.Add(row);
        }

        _byVendor = accepted
            .GroupBy(r => r.CreatureTemplateId.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<VendorStockView>)g.OrderBy(r => r.Sequence).Select(ToView).ToList());

        RowCount = accepted.Count;
        Refused = refused;

        foreach (VendorStockRefusal refusal in refused)
        {
            logger.LogError("Refused {Refusal}. The vendor sells its other rows", refusal.ToString());
        }

        logger.LogInformation("Loaded {RowCount} stock rows for {VendorCount} vendors; refused {RefusedCount}",
            RowCount, VendorCount, Refused.Count);
    }

    public int VendorCount => _byVendor.Count;

    public int RowCount { get; }

    public IReadOnlyList<VendorStockRefusal> Refused { get; }

    public bool TryGet(CreatureTemplateId templateId, [NotNullWhen(true)] out IReadOnlyList<VendorStockView>? rows) =>
        _byVendor.TryGetValue(templateId.Value, out rows);

    /// <summary>The template's rows, or <see cref="NoRows" />. No allocation: read every tick.</summary>
    public IReadOnlyList<VendorStockView> RowsFor(CreatureTemplateId templateId) =>
        _byVendor.TryGetValue(templateId.Value, out IReadOnlyList<VendorStockView>? rows) ? rows : NoRows;

    /// <summary>For the game master's reload reply.</summary>
    public string Describe()
    {
        string counts = $"{VendorCount} vendors, {RowCount} rows";
        return Refused.Count == 0
            ? counts
            : $"{counts}, {Refused.Count} refused ({string.Join("; ", Refused)})";
    }

    private static string? Problem(VendorStock row, HashSet<ulong> items)
    {
        if (!items.Contains(row.ItemTemplateId.Value))
            return $"item template {row.ItemTemplateId.Value} does not exist";

        if (row.MaxStock is not null && row.RestockSeconds is null)
            return "MaxStock is set without RestockSeconds";
        if (row.MaxStock is null && row.RestockSeconds is not null)
            return "RestockSeconds is set without MaxStock";
        if (row.MaxStock == 0)
            return "MaxStock is 0; a limited row holds at least 1";
        if (row.RestockSeconds == 0)
            return "RestockSeconds is 0; a restock takes at least a second";

        if ((row.RequiredQuestId is null) != (row.RequiredQuestState is null))
            return "RequiredQuestId and RequiredQuestState must be set together";

        HashSet<ulong> costItems = [];
        foreach (VendorStockCost cost in row.Costs.OrderBy(c => c.ItemTemplateId.Value))
        {
            if (!items.Contains(cost.ItemTemplateId.Value))
                return $"cost item template {cost.ItemTemplateId.Value} does not exist";
            if (cost.Count == 0)
                return $"cost of item template {cost.ItemTemplateId.Value} has Count 0";
            if (!costItems.Add(cost.ItemTemplateId.Value))
                return $"names cost item template {cost.ItemTemplateId.Value} twice";
        }

        return null;
    }

    private static VendorStockView ToView(VendorStock row) => new(
        row.Id,
        row.Sequence,
        row.ItemTemplateId,
        row.MaxStock,
        row.RestockSeconds,
        row.PriceOverride,
        row is { RequiredQuestId: { } quest, RequiredQuestState: { } state } ? new QuestRequirement(quest, state) : null,
        row.Costs
            .OrderBy(c => c.ItemTemplateId.Value)
            .Select(c => new VendorCostView(c.ItemTemplateId, c.Count))
            .ToList());
}
