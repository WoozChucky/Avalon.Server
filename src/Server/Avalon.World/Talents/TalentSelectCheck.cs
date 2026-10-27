using System.Buffers;
using Avalon.Common;
using Avalon.Domain.World;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Talents;

/// <summary>
/// The check a character's saved talents pass at select, against the live tree (#1181): a later tree edit may have
/// removed a node, cut a held node off from the class start, or left more nodes held than the points earned. Only what
/// no longer fits is dropped, for free, each drop marking the save (<see cref="TalentAllocation.Drop" />), so the next
/// save writes the allocation the character now holds.
/// </summary>
public static class TalentSelectCheck
{
    /// <summary>
    /// Drops, in order: the held nodes <paramref name="tree" /> does not hold, and any Start; then the held nodes a walk
    /// from <paramref name="class" />'s start over held nodes does not reach; then, if the nodes still held cost more
    /// than <paramref name="earned" />, every node. Each step that drops anything logs one Warning with the count and
    /// the ids (the caller's scope names the character). A tree that is not loaded (refused at startup, a bad deploy)
    /// drops nothing: the rows stay as they are, inert, until a tree that holds them is loaded again.
    /// </summary>
    public static void Apply(TalentAllocation talents, TalentCatalog tree, CharacterClass @class, int earned,
        ILogger logger)
    {
        if (!tree.IsLoaded || talents.Spent == 0)
            return;

        List<int>? dropped = null;

        // 1. A node the tree no longer holds, or a Start: a start is the class's own, never allocated.
        IReadOnlyList<int> held = talents.Held;
        for (int i = 0; i < held.Count; i++)
        {
            if (!tree.TryIndexOf(held[i], out int index) || tree.KindAt(index) == TalentNodeKind.Start)
                (dropped ??= []).Add(held[i]);
        }

        Drop(talents, dropped, logger, "the tree no longer holds them as nodes a character may allocate");

        // 2. A node no path of held nodes links to the class's start.
        dropped = Unreached(talents, tree, @class);
        Drop(talents, dropped, logger, "no path of held nodes links them to the class's start");

        // 3. More nodes held than the points earned: the whole allocation goes, so the player spends the points again.
        if (talents.Spent > earned)
        {
            dropped = [.. talents.Held];
            Drop(talents, dropped, logger, $"they cost more than the {earned} points earned");
        }
    }

    /// <summary>The held nodes the walk from the class's start does not reach; null when it reaches every one.</summary>
    private static List<int>? Unreached(TalentAllocation talents, TalentCatalog tree, CharacterClass @class)
    {
        if (talents.Spent == 0)
            return null;

        int start = tree.StartIndexOf(@class);
        if (start < 0)
            return [.. talents.Held];   // a loaded tree has a start for every class, so a class without one reaches nothing

        int words = tree.BitWords;
        ulong[] heldBits = ArrayPool<ulong>.Shared.Rent(words);
        ulong[] reached = ArrayPool<ulong>.Shared.Rent(words);
        int[] queue = ArrayPool<int>.Shared.Rent(tree.Count);
        try
        {
            Array.Clear(heldBits, 0, words);
            IReadOnlyList<int> held = talents.Held;
            for (int i = 0; i < held.Count; i++)
            {
                // Step 1 left only nodes the tree holds.
                tree.TryIndexOf(held[i], out int index);
                heldBits[index >> 6] |= 1UL << (index & 63);
            }

            tree.WalkHeld(start, heldBits.AsSpan(0, words), reached.AsSpan(0, words), queue.AsSpan(0, tree.Count));

            List<int>? unreached = null;
            for (int i = 0; i < held.Count; i++)
            {
                tree.TryIndexOf(held[i], out int index);
                if ((reached[index >> 6] & (1UL << (index & 63))) == 0)
                    (unreached ??= []).Add(held[i]);
            }

            return unreached;
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(heldBits);
            ArrayPool<ulong>.Shared.Return(reached);
            ArrayPool<int>.Shared.Return(queue);
        }
    }

    private static void Drop(TalentAllocation talents, List<int>? dropped, ILogger logger, string reason)
    {
        if (dropped is null)
            return;

        foreach (int nodeId in dropped)
            talents.Drop(nodeId);

        logger.LogWarning("Dropped {Count} saved talent nodes at select, free, because {Reason}: {NodeIds}",
            dropped.Count, reason, dropped);
    }
}
