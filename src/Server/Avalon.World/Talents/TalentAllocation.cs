using Avalon.Combat;
using Avalon.Domain.Characters;
using Avalon.World.Persistence;
using Avalon.World.Threading;

namespace Avalon.World.Talents;

/// <summary>
/// The talent nodes one character holds (#1181), and their stat lines summed into <see cref="Totals" />. World-side and
/// tick-thread only, never on ICharacter, so no mod can read or change it. Memory is authoritative: the tree changes only
/// through this class, every allocation, refund and drop marks the save (<see cref="SaveStateTracker.TalentChanged" />),
/// and the save writes what it holds; <see cref="Load" /> marks nothing. It does not decide what may be allocated or
/// refunded: the talent rules do. Its mutators assert the tick thread while <see cref="TickThreadGuard" /> is enabled.
/// </summary>
/// <remarks>
/// The held node ids are a list kept ascending, with membership by binary search: no set, and nothing allocated by a
/// lookup or a walk. The totals are one buffer rebuilt in place by <see cref="RebuildTotals" /> after a change, never per
/// tick or per refresh.
/// </remarks>
public sealed class TalentAllocation(SaveStateTracker save, TickThreadGuard? tickThread = null)
{
    private readonly List<int> _held = [];

    /// <summary>The held node ids, ascending.</summary>
    public IReadOnlyList<int> Held => _held;

    /// <summary>The points spent: one per held node.</summary>
    public int Spent => _held.Count;

    /// <summary>Moves on every load and every change of what is held.</summary>
    public int Version { get; private set; }

    /// <summary>The held nodes' stat lines, as of the last <see cref="RebuildTotals" />.</summary>
    public TalentTotals Totals { get; } = new();

    /// <summary>The talent window's state changed since it was last sent: the flusher sends it.</summary>
    public bool StateOwed { get; private set; }

    /// <summary>The character's level when the talent window's state was last sent.</summary>
    public ushort StateSentLevel { get; private set; }

    /// <summary>
    /// The abilities held changed since their list was last sent: the flusher sends it, once per tick however many
    /// changes the tick made (Part C).
    /// </summary>
    public bool AbilitiesOwed { get; private set; }

    public bool Holds(int nodeId) => _held.BinarySearch(nodeId) >= 0;

    /// <summary>Replaces what is held with the stored rows, each node once. Marks nothing.</summary>
    public void Load(IReadOnlyList<CharacterTalent> rows)
    {
        tickThread?.AssertOnTick("TalentAllocation.Load");
        _held.Clear();
        for (int i = 0; i < rows.Count; i++)
            Insert(rows[i].NodeId);

        Version++;
    }

    /// <summary>
    /// Holds each node not held yet and marks it for the save. The talent rules have accepted the ids, so each is a node
    /// of the tree, and so a positive <see cref="int" />. Every id is checked before anything changes, so one that is not
    /// throws and leaves what is held as it was.
    /// </summary>
    public void Allocate(IReadOnlyList<uint> nodeIds)
    {
        tickThread?.AssertOnTick("TalentAllocation.Allocate");
        for (int i = 0; i < nodeIds.Count; i++)
        {
            if (nodeIds[i] is 0 or > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(nodeIds), nodeIds[i], "A talent node id is a positive int.");
        }

        bool changed = false;
        for (int i = 0; i < nodeIds.Count; i++)
        {
            int nodeId = (int)nodeIds[i];
            if (!Insert(nodeId))
                continue;

            save.TalentChanged(nodeId);
            changed = true;
        }

        if (changed)
            Version++;
    }

    /// <summary>
    /// Holds each node no more and marks it for the save; an id not held is skipped. The talent rules have accepted the
    /// ids, so each is a held node of the tree, and so a positive <see cref="int" />. Every id is checked before anything
    /// changes, so one that is not throws and leaves what is held as it was.
    /// </summary>
    public void Refund(IReadOnlyList<uint> nodeIds)
    {
        tickThread?.AssertOnTick("TalentAllocation.Refund");
        for (int i = 0; i < nodeIds.Count; i++)
        {
            if (nodeIds[i] is 0 or > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(nodeIds), nodeIds[i], "A talent node id is a positive int.");
        }

        bool changed = false;
        for (int i = 0; i < nodeIds.Count; i++)
        {
            int nodeId = (int)nodeIds[i];
            int index = _held.BinarySearch(nodeId);
            if (index < 0)
                continue;

            _held.RemoveAt(index);
            save.TalentChanged(nodeId);
            changed = true;
        }

        if (changed)
            Version++;
    }

    /// <summary>Holds the node no more and marks it for the save; nothing when it was not held.</summary>
    public void Drop(int nodeId)
    {
        tickThread?.AssertOnTick("TalentAllocation.Drop");
        int index = _held.BinarySearch(nodeId);
        if (index < 0)
            return;

        _held.RemoveAt(index);
        save.TalentChanged(nodeId);
        Version++;
    }

    /// <summary>
    /// <see cref="Totals" /> rebuilt from every held node the tree holds; a held node it does not hold adds nothing.
    /// </summary>
    public void RebuildTotals(TalentCatalog tree)
    {
        tickThread?.AssertOnTick("TalentAllocation.RebuildTotals");
        Totals.Clear();
        for (int i = 0; i < _held.Count; i++)
        {
            if (!tree.TryIndexOf(_held[i], out int index))
                continue;

            foreach (TalentLine line in tree.LinesAt(index))
                Totals.Add(line.Stat, line.Kind, line.Value, line.ValueMax);
        }
    }

    public void MarkStateOwed() => StateOwed = true;

    public void MarkAbilitiesOwed() => AbilitiesOwed = true;

    /// <summary>The ability list was sent: nothing is owed until the abilities held change again.</summary>
    public void AbilitiesSent() => AbilitiesOwed = false;

    /// <summary>The state was sent at <paramref name="level" />: nothing is owed until it changes again.</summary>
    public void StateSent(ushort level)
    {
        StateOwed = false;
        StateSentLevel = level;
    }

    /// <summary>Inserts the node at its place in the ascending list; false when it is held already.</summary>
    private bool Insert(int nodeId)
    {
        int index = _held.BinarySearch(nodeId);
        if (index >= 0)
            return false;

        _held.Insert(~index, nodeId);
        return true;
    }
}
