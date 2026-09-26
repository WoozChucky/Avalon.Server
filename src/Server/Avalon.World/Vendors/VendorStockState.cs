using Avalon.Common.ValueObjects;

namespace Avalon.World.Vendors;

/// <summary>
/// One vendor creature's live stock (spec #432): for each limited row, how many are left and when
/// it refills. It is held in memory on the town instance, shared by everyone who shops there, and
/// reset by a restart. Unlimited rows keep no state. Tick thread only.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The timer starts when a sale takes a full row below its maximum, and later sales do not
/// push it back.</item>
/// <item>A reload that leaves a row below its maximum with no timer running (a raised MaxStock)
/// starts the timer on the next pass, so the row refills without waiting for a sale.</item>
/// <item>When the timer is due, <see cref="Restock" /> refills the row to MaxStock.</item>
/// <item>Time is absolute, so an instance that did not tick while empty catches up on its next tick.</item>
/// </list>
/// </remarks>
public sealed class VendorStockState
{
    private Dictionary<int, Limited> _limited = [];

    public VendorStockState(CreatureTemplateId template, IReadOnlyList<VendorStockView> rows)
    {
        Template = template;
        Rows = rows;

        foreach (VendorStockView row in rows)
        {
            if (row is { MaxStock: { } max, RestockSeconds: { } seconds })
                _limited[row.Id] = new Limited(max, seconds);
        }
    }

    /// <summary>The creature template whose catalog rows this holds.</summary>
    public CreatureTemplateId Template { get; }

    /// <summary>The catalog rows this state was last reconciled with, in Sequence order.</summary>
    public IReadOnlyList<VendorStockView> Rows { get; private set; }

    /// <summary>
    /// A count or the rows changed since the last <see cref="ClearChanged" />, so every open shop
    /// of this vendor is owed a new list.
    /// </summary>
    public bool Changed { get; private set; }

    /// <summary>How many of a row are left: null for an unlimited row.</summary>
    public uint? Available(VendorStockView row)
    {
        if (!row.IsLimited)
            return null;

        return _limited.TryGetValue(row.Id, out Limited? limited) ? limited.Count : 0u;
    }

    /// <summary>
    /// Takes <paramref name="count" /> of a limited row, and starts its timer if none is running.
    /// Does nothing for an unlimited row. The caller has checked <see cref="Available" /> first;
    /// taking more than is left throws, and so does taking from a limited row this state does not
    /// track, which <see cref="Available" /> reports as 0 left (#432).
    /// </summary>
    public void Take(VendorStockView row, uint count, DateTime now)
    {
        if (!row.IsLimited)
            return;

        if (!_limited.TryGetValue(row.Id, out Limited? limited))
            throw new InvalidOperationException(
                $"Taking {count} of limited stock row {row.Id}, which this vendor's state does not track; the caller resolves rows from Rows.");

        if (count == 0)
            return;

        if (count > limited.Count)
            throw new InvalidOperationException(
                $"Taking {count} of stock row {row.Id}, which has {limited.Count} left; the caller checks Available first.");

        limited.Count -= count;
        limited.RestockAt ??= now.AddSeconds(limited.RestockSeconds);
        limited.TimerOwed = false;
        Changed = true;
    }

    /// <summary>
    /// Starts every timer a reload left owed, then refills every row whose timer is due.
    /// Allocation-free: runs on every vendor pass.
    /// </summary>
    public void Restock(DateTime now)
    {
        foreach (Limited limited in _limited.Values)
        {
            if (limited.TimerOwed)
            {
                limited.RestockAt ??= now.AddSeconds(limited.RestockSeconds);
                limited.TimerOwed = false;
            }

            if (limited.RestockAt is { } due && now >= due)
            {
                limited.Count = limited.Max;
                limited.RestockAt = null;
                Changed = true;
            }
        }
    }

    /// <summary>
    /// Adopts a reloaded catalog's rows. Nothing happens when <paramref name="rows" /> is the list
    /// already held, which is every pass without a reload. Otherwise:
    /// <list type="bullet">
    /// <item>counts carry over by row id, clamped to the new MaxStock;</item>
    /// <item>a running timer survives only while the row is still below its maximum;</item>
    /// <item>a row below its maximum with no timer running (its MaxStock was raised) is owed one,
    /// started by the next <see cref="Restock" />;</item>
    /// <item>new limited rows start full;</item>
    /// <item>removed or now-unlimited rows are dropped.</item>
    /// </list>
    /// </summary>
    public void Reconcile(IReadOnlyList<VendorStockView> rows)
    {
        if (ReferenceEquals(rows, Rows))
            return;

        Dictionary<int, Limited> next = [];
        foreach (VendorStockView row in rows)
        {
            if (row is not { MaxStock: { } max, RestockSeconds: { } seconds })
                continue;

            var limited = new Limited(max, seconds);
            if (_limited.TryGetValue(row.Id, out Limited? old))
            {
                limited.Count = Math.Min(old.Count, max);
                if (limited.Count < max)
                {
                    limited.RestockAt = old.RestockAt;
                    limited.TimerOwed = old.RestockAt is null;
                }
            }

            next[row.Id] = limited;
        }

        _limited = next;
        Rows = rows;
        Changed = true;
    }

    /// <summary>Owes every open shop of this vendor a new list: a /reload items changed what a list shows.</summary>
    public void MarkChanged() => Changed = true;

    public void ClearChanged() => Changed = false;

    /// <summary>A reference type, so a pass can change its fields while it walks the dictionary.</summary>
    private sealed class Limited(uint max, uint restockSeconds)
    {
        public uint Max { get; } = max;

        public uint RestockSeconds { get; } = restockSeconds;

        public uint Count { get; set; } = max;

        public DateTime? RestockAt { get; set; }

        /// <summary>A reload left the row below its maximum with no timer; the next pass starts one.</summary>
        public bool TimerOwed { get; set; }
    }
}
