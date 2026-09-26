using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One line of a vendor's shop (#432): what a creature template sells, at what price, whether its
/// count is limited and how it restocks, and whether a quest hides it. Ids are chosen by whoever
/// authors the data, not generated.
/// </summary>
/// <remarks>
/// Check constraints pair the optional columns: <see cref="MaxStock" /> and
/// <see cref="RestockSeconds" /> are both set or both null, and so are <see cref="RequiredQuestId" />
/// and <see cref="RequiredQuestState" />. VendorCatalog validates the same rules on load.
/// </remarks>
public class VendorStock
{
    public int Id { get; set; }

    /// <summary>The vendor. Any creature whose dialogue offers OpenShop sells its template's rows.</summary>
    public CreatureTemplateId CreatureTemplateId { get; set; } = default!;

    /// <summary>List order, and what CVendorBuyPacket names. Unique per creature template.</summary>
    public uint Sequence { get; set; }

    public ItemTemplateId ItemTemplateId { get; set; } = default!;

    /// <summary>Null means unlimited. Otherwise the count every restock refills to, at least 1.</summary>
    public uint? MaxStock { get; set; }

    /// <summary>Seconds from the first sale below MaxStock to the refill. Set exactly when MaxStock is.</summary>
    public uint? RestockSeconds { get; set; }

    /// <summary>Copper per unit. Null means the item's BuyPrice.</summary>
    public uint? PriceOverride { get; set; }

    /// <summary>A QuestTemplate id. No foreign key: the quest system (#433) will reshape that table.</summary>
    public uint? RequiredQuestId { get; set; }

    public QuestRequirementState? RequiredQuestState { get; set; }

    /// <summary>Items taken from the Bag per unit bought, on top of the price. Usually none.</summary>
    public List<VendorStockCost> Costs { get; set; } = [];
}
