using System.Diagnostics.CodeAnalysis;
using Avalon.Common;
using Avalon.Common.ValueObjects;

namespace Avalon.World.Vendors;

/// <summary>
/// A town instance's vendor stock (spec #432): one <see cref="VendorStockState" /> per vendor
/// creature. A state is created, full, the first time a shop opens with that vendor in this
/// instance. It is never removed: script hot reload removes and re-adds creatures, and dropping
/// the state there would restock the vendor. It dies with the instance, so a restart resets it.
/// Tick thread only.
/// </summary>
public sealed class VendorStocks
{
    private readonly Dictionary<ObjectGuid, VendorStockState> _states = [];

    /// <summary>How many vendors have stock here. The instance skips its vendor pass at 0.</summary>
    public int Count => _states.Count;

    /// <summary>The vendor's state, created full from <paramref name="rows" /> or reconciled with them.</summary>
    public VendorStockState For(ObjectGuid vendor, CreatureTemplateId template, IReadOnlyList<VendorStockView> rows)
    {
        if (_states.TryGetValue(vendor, out VendorStockState? state))
        {
            state.Reconcile(rows);
            return state;
        }

        state = new VendorStockState(template, rows);
        _states[vendor] = state;
        return state;
    }

    public bool TryGet(ObjectGuid vendor, [NotNullWhen(true)] out VendorStockState? state) =>
        _states.TryGetValue(vendor, out state);

    /// <summary>
    /// The vendor pass. It reconciles each state with the current catalog, which is a reference
    /// comparison unless a reload landed, then refills what is due. Allocation-free when nothing
    /// changed.
    /// </summary>
    public void Update(DateTime now, VendorCatalog catalog)
    {
        foreach (VendorStockState state in _states.Values)
        {
            state.Reconcile(catalog.RowsFor(state.Template));
            state.Restock(now);
        }
    }

    /// <summary>After every open shop has heard this pass's lists.</summary>
    public void ClearChanged()
    {
        foreach (VendorStockState state in _states.Values)
            state.ClearChanged();
    }
}
