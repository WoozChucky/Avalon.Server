using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>One item a <see cref="VendorStock" /> row costs per unit bought. Keyed by (row, item), so a row names each item once.</summary>
public class VendorStockCost
{
    public int VendorStockId { get; set; }

    public ItemTemplateId ItemTemplateId { get; set; } = default!;

    /// <summary>At least 1, per unit bought.</summary>
    public uint Count { get; set; }
}
