using System.Buffers;
using System.Numerics;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Talents;

namespace Avalon.World.Talents;

/// <summary>
/// Whether a talent allocation or refund (#1181) may go ahead, all or nothing. The rules only decide: they change
/// nothing, mark no save and allocate nothing, so a refusal leaves the character exactly as it was.
/// </summary>
public static class TalentRules
{
    /// <summary>
    /// The first rule <paramref name="nodeIds" /> breaks, in this order, or Ok:
    /// <list type="number">
    /// <item><see cref="TalentResult.Dead" />;</item>
    /// <item><see cref="TalentResult.TooMany" />: more ids than <paramref name="earned" /> less the points spent (so any
    /// id at all when none are left), or than the tree holds;</item>
    /// <item>then each id in the order sent: <see cref="TalentResult.UnknownNode" /> (0, past <see cref="int.MaxValue" />,
    /// or not in the tree), <see cref="TalentResult.AlreadyAllocated" /> (held, named earlier in the request, or a
    /// start), <see cref="TalentResult.NotConnected" /> (no neighbour that is the class's start, a held node or a node
    /// named earlier) and <see cref="TalentResult.ForkLocked" /> (its fork partner held or named earlier).</item>
    /// </list>
    /// A refusal of an id names it in <paramref name="refusedNode" />, which is 0 otherwise. An empty list is Ok.
    /// "Named earlier" is one bit per tree index in a pooled array (ArrayPool, as <see cref="TalentSelectCheck" />),
    /// set as each id passes, so a request costs O(n · degree) and allocates nothing once the pool is warm.
    /// </summary>
    public static TalentResult Allocate(TalentAllocation talents, TalentCatalog tree, CharacterClass @class, bool dead,
        int earned, IReadOnlyList<uint> nodeIds, out uint refusedNode)
    {
        refusedNode = 0;
        if (dead)
            return TalentResult.Dead;

        int count = nodeIds.Count;
        if (count > earned - talents.Spent || count > tree.Count)
            return count == 0 ? TalentResult.Ok : TalentResult.TooMany;

        if (count == 0)
            return TalentResult.Ok;

        int start = tree.StartIndexOf(@class);
        int words = tree.BitWords;
        ulong[] named = ArrayPool<ulong>.Shared.Rent(words);
        try
        {
            Array.Clear(named, 0, words);
            for (int i = 0; i < count; i++)
            {
                uint id = nodeIds[i];
                TalentResult result = Check(talents, tree, start, named, id, out int index);
                if (result != TalentResult.Ok)
                {
                    refusedNode = id;
                    return result;
                }

                named[index >> 6] |= 1UL << (index & 63);
            }

            return TalentResult.Ok;
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(named);
        }
    }

    /// <summary>
    /// The first rule a refund of <paramref name="nodeIds" /> (Part C) breaks, in this order, or Ok:
    /// <list type="number">
    /// <item>each id in the order sent: <see cref="TalentResult.UnknownNode" /> (0, past <see cref="int.MaxValue" />, or
    /// not in the tree, so every id while the tree is refused) and <see cref="TalentResult.NotAllocated" /> (not held,
    /// or named earlier in the request; a start is never held);</item>
    /// <item><see cref="TalentResult.WouldDisconnect" />: a held node that a path of held nodes linked to the class's
    /// start before the refund and none links after it. A node no path reached before does not block the refund.</item>
    /// </list>
    /// A refusal names in <paramref name="refusedNode" /> the id as sent, or for WouldDisconnect the lowest-id node the
    /// refund would strand; 0 otherwise. An empty list is Ok. The rules only decide: nothing changes and nothing is
    /// marked. The check walks the tree twice, before and after, over four pooled bit sets of one bit per tree index
    /// (held, named, reached before, reached after) and a pooled queue, every one returned on every path, so a request
    /// allocates nothing once the pool is warm.
    /// </summary>
    public static TalentResult Refund(TalentAllocation talents, TalentCatalog tree, CharacterClass @class,
        IReadOnlyList<uint> nodeIds, out uint refusedNode)
    {
        refusedNode = 0;
        int count = nodeIds.Count;
        if (count == 0)
            return TalentResult.Ok;

        int words = tree.BitWords;
        ulong[] named = ArrayPool<ulong>.Shared.Rent(words);
        ulong[] held = ArrayPool<ulong>.Shared.Rent(words);
        ulong[] before = ArrayPool<ulong>.Shared.Rent(words);
        ulong[] after = ArrayPool<ulong>.Shared.Rent(words);
        int[] queue = ArrayPool<int>.Shared.Rent(tree.Count);
        try
        {
            Array.Clear(named, 0, words);
            for (int i = 0; i < count; i++)
            {
                uint id = nodeIds[i];
                if (id is 0 or > int.MaxValue || !tree.TryIndexOf((int)id, out int index))
                {
                    refusedNode = id;
                    return TalentResult.UnknownNode;
                }

                if (!talents.Holds((int)id) || IsSet(named, index))
                {
                    refusedNode = id;
                    return TalentResult.NotAllocated;
                }

                named[index >> 6] |= 1UL << (index & 63);
            }

            int stranded = Stranded(talents, tree, tree.StartIndexOf(@class), named.AsSpan(0, words),
                held.AsSpan(0, words), before.AsSpan(0, words), after.AsSpan(0, words), queue.AsSpan(0, tree.Count));
            if (stranded < 0)
                return TalentResult.Ok;

            refusedNode = (uint)tree.NodeIdAt(stranded);
            return TalentResult.WouldDisconnect;
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(named);
            ArrayPool<ulong>.Shared.Return(held);
            ArrayPool<ulong>.Shared.Return(before);
            ArrayPool<ulong>.Shared.Return(after);
            ArrayPool<int>.Shared.Return(queue);
        }
    }

    /// <summary>
    /// The lowest tree index of a held node that the walk from <paramref name="start" /> over held nodes reaches before
    /// the nodes in <paramref name="named" /> leave and does not reach after; -1 when there is none. Tree indices
    /// ascend with node ids, so the lowest index is the lowest id. A start of -1 (no start for the class, which a loaded
    /// tree never has) reaches nothing either way, so it strands nothing.
    /// </summary>
    private static int Stranded(TalentAllocation talents, TalentCatalog tree, int start, ReadOnlySpan<ulong> named,
        Span<ulong> held, Span<ulong> before, Span<ulong> after, Span<int> queue)
    {
        if (start < 0)
            return -1;

        held.Clear();
        IReadOnlyList<int> heldIds = talents.Held;
        for (int i = 0; i < heldIds.Count; i++)
        {
            // A held node the tree lacks is skipped: the select check drops it at the next select.
            if (tree.TryIndexOf(heldIds[i], out int index))
                held[index >> 6] |= 1UL << (index & 63);
        }

        tree.WalkHeld(start, held, before, queue);
        for (int w = 0; w < held.Length; w++)
            held[w] &= ~named[w];

        tree.WalkHeld(start, held, after, queue);
        for (int w = 0; w < held.Length; w++)
        {
            ulong stranded = before[w] & held[w] & ~after[w];
            if (stranded != 0)
                return (w << 6) + BitOperations.TrailingZeroCount(stranded);
        }

        return -1;
    }

    /// <summary>
    /// The first rule <paramref name="id" /> breaks, given the ids before it (<paramref name="named" />), or Ok, with
    /// its tree index.
    /// </summary>
    private static TalentResult Check(TalentAllocation talents, TalentCatalog tree, int start, ulong[] named, uint id,
        out int index)
    {
        if (id is 0 or > int.MaxValue || !tree.TryIndexOf((int)id, out index))
        {
            index = -1;
            return TalentResult.UnknownNode;
        }

        if (talents.Holds((int)id) || IsSet(named, index) || tree.KindAt(index) == TalentNodeKind.Start)
            return TalentResult.AlreadyAllocated;

        if (!Connected(talents, tree, start, named, index))
            return TalentResult.NotConnected;

        int partner = tree.ForkPartnerAt(index);
        if (partner >= 0 && Reached(talents, tree, named, partner))
            return TalentResult.ForkLocked;

        return TalentResult.Ok;
    }

    /// <summary>Whether a neighbour of the node is the class's start, a held node, or a node named before it.</summary>
    private static bool Connected(TalentAllocation talents, TalentCatalog tree, int start, ulong[] named, int index)
    {
        foreach (int neighbour in tree.NeighboursAt(index))
        {
            if (neighbour == start || Reached(talents, tree, named, neighbour))
                return true;
        }

        return false;
    }

    /// <summary>Whether the node at <paramref name="index" /> is held, or named earlier in the request.</summary>
    private static bool Reached(TalentAllocation talents, TalentCatalog tree, ulong[] named, int index) =>
        IsSet(named, index) || talents.Holds(tree.NodeIdAt(index));

    private static bool IsSet(ulong[] bits, int index) => (bits[index >> 6] & (1UL << (index & 63))) != 0;
}
