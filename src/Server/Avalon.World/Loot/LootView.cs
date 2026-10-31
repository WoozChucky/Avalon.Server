using System.Runtime.InteropServices;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Instances;
using Avalon.World.Public.Instances;

namespace Avalon.World.Loot;

/// <summary>
/// The drops one client has been told lie on the ground (#1269), by their raw guids, and what its next loot packets
/// carry. A drop enters the view within the interest radius and leaves it beyond radius + margin, by the rule every other
/// object's view follows (<see cref="Interest.IsVisible{TTracked}" />). Its instance decides it on the view's cadence
/// (<see cref="Decide" />), tells it of what a kill places in view (<see cref="Learn" />) and of what is picked up
/// (<see cref="Forget" />). World-side and tick thread only; it holds no <see cref="GroundLoot" /> between a decision and
/// <see cref="Sent" />, so a drop picked up is never kept alive by a view.
/// </summary>
internal sealed class LootView
{
    // Each held drop's raw guid with the decision that last saw it in view, the stamps of EntityTrackingSystem.Update.
    private readonly Dictionary<ulong, uint> _known = [];
    private readonly List<GroundLoot> _entered = [];
    private readonly List<ulong> _forgotten = [];
    private uint _frame;

    /// <summary>How many drops the client holds.</summary>
    public int Count => _known.Count;

    /// <summary>The drops the last <see cref="Decide" /> found newly in view, now held, until <see cref="Sent" />.</summary>
    public IReadOnlyList<GroundLoot> Entered => _entered;

    /// <summary>The raw guids of the drops the last <see cref="Decide" /> found out of view, no longer held, until <see cref="Sent" />.</summary>
    public List<ulong> Left { get; } = [];

    /// <summary>Whether <paramref name="drop" /> is in this view by the interest rule, the margin counting only for a drop already held.</summary>
    public bool Sees(ObjectGuid watcher, Vector3 watcherPosition, GroundLoot drop, InterestRange range) =>
        Interest.IsVisible(watcher, watcherPosition, drop.Guid, drop.Position, range, new KnownLoot(_known));

    /// <summary>Holds <paramref name="drop" />: the client is being told of it.</summary>
    public void Learn(GroundLoot drop) => _known[drop.Guid.RawValue] = _frame;

    /// <summary>Lets go of the drop of <paramref name="rawGuid" />, picked up; true when the client held it.</summary>
    public bool Forget(ulong rawGuid) => _known.Remove(rawGuid);

    /// <summary>
    /// Decides the view again: every drop of <paramref name="store" /> within reach of the watcher that is in view and was
    /// not held goes into <see cref="Entered" /> and is held, and every held drop not in view now, picked up without a
    /// <see cref="Forget" /> included, goes into <see cref="Left" /> and is let go. Both start empty. Allocates nothing
    /// once its lists have grown.
    /// </summary>
    public void Decide(ObjectGuid watcher, Vector3 watcherPosition, InterestRange range, GroundLootStore store)
    {
        _entered.Clear();
        Left.Clear();

        _frame = unchecked(_frame + 1);
        if (_frame == 0)
        {
            // Rebase on wrap without losing what is held: replacing a value does not invalidate the enumerator.
            foreach (KeyValuePair<ulong, uint> entry in _known)
                CollectionsMarshal.GetValueRefOrNullRef(_known, entry.Key) = 0;
            _frame = 1;
        }

        var candidates = new Candidates(this, watcher, watcherPosition, range);
        store.Visit(watcherPosition.x, watcherPosition.z, Interest.Reach(range), ref candidates);

        // Every drop stamped is held, so equal counts mean nothing left.
        if (candidates.Seen == _known.Count)
            return;

        foreach (KeyValuePair<ulong, uint> entry in _known)
        {
            if (entry.Value != _frame)
                Left.Add(entry.Key);
        }

        for (int i = 0; i < Left.Count; i++)
            _known.Remove(Left[i]);
    }

    /// <summary>The client was told of <see cref="Entered" /> and <see cref="Left" />: both are emptied.</summary>
    public void Sent()
    {
        _entered.Clear();
        Left.Clear();
    }

    /// <summary>
    /// Forgets every drop held, for a client starting over, and returns their raw guids, with those of <see cref="Left" />
    /// not yet sent: a broadcast that threw after the decision never told the client they left, so it may still hold them
    /// (the client ignores a guid it does not hold). The list is reused: it is valid until the next call to this.
    /// </summary>
    public List<ulong> Reset()
    {
        _forgotten.Clear();
        _forgotten.AddRange(Left);
        foreach (KeyValuePair<ulong, uint> entry in _known)
            _forgotten.Add(entry.Key);

        _entered.Clear();
        Left.Clear();
        _known.Clear();
        return _forgotten;
    }

    // Stamps every drop in view with this decision, and lists those not held before.
    private struct Candidates(LootView view, ObjectGuid watcher, Vector3 watcherPosition, InterestRange range)
        : ILootVisitor
    {
        public int Seen;

        public void Visit(GroundLoot drop)
        {
            if (!view.Sees(watcher, watcherPosition, drop, range))
                return;

            ref uint stamp = ref CollectionsMarshal.GetValueRefOrAddDefault(view._known, drop.Guid.RawValue, out bool held);
            if (!held)
                view._entered.Add(drop);
            else if (stamp == view._frame)
                return;   // seen already in this decision: a guid is counted once

            stamp = view._frame;
            Seen++;
        }
    }

    private readonly struct KnownLoot(Dictionary<ulong, uint> known) : Interest.ITrackedSet
    {
        public bool Contains(ObjectGuid obj) => known.ContainsKey(obj.RawValue);
    }
}
