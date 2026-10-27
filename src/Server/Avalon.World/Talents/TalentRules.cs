using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Talents;

namespace Avalon.World.Talents;

/// <summary>
/// Whether a talent allocation (#1181) may go ahead, all or nothing. The rules only decide: they change nothing, mark no
/// save and allocate nothing, so a refusal leaves the character exactly as it was.
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
    /// "Named earlier" is a scan of the ids before it, at most the points available, so no set is built.
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

        int start = tree.StartIndexOf(@class);
        for (int i = 0; i < count; i++)
        {
            uint id = nodeIds[i];
            TalentResult result = Check(talents, tree, start, nodeIds, i, id);
            if (result == TalentResult.Ok)
                continue;

            refusedNode = id;
            return result;
        }

        return TalentResult.Ok;
    }

    /// <summary>The first rule the id at <paramref name="position" /> breaks, given the ids before it, or Ok.</summary>
    private static TalentResult Check(TalentAllocation talents, TalentCatalog tree, int start, IReadOnlyList<uint> nodeIds,
        int position, uint id)
    {
        if (id is 0 or > int.MaxValue || !tree.TryIndexOf((int)id, out int index))
            return TalentResult.UnknownNode;

        if (talents.Holds((int)id) || NamedBefore(nodeIds, position, id) || tree.KindAt(index) == TalentNodeKind.Start)
            return TalentResult.AlreadyAllocated;

        if (!Connected(talents, tree, start, nodeIds, position, index))
            return TalentResult.NotConnected;

        int partner = tree.ForkPartnerAt(index);
        if (partner >= 0 && Reached(talents, tree, nodeIds, position, partner))
            return TalentResult.ForkLocked;

        return TalentResult.Ok;
    }

    /// <summary>Whether a neighbour of the node is the class's start, a held node, or a node named before it.</summary>
    private static bool Connected(TalentAllocation talents, TalentCatalog tree, int start, IReadOnlyList<uint> nodeIds,
        int position, int index)
    {
        foreach (int neighbour in tree.NeighboursAt(index))
        {
            if (neighbour == start || Reached(talents, tree, nodeIds, position, neighbour))
                return true;
        }

        return false;
    }

    /// <summary>Whether the node at <paramref name="index" /> is held, or named before <paramref name="position" />.</summary>
    private static bool Reached(TalentAllocation talents, TalentCatalog tree, IReadOnlyList<uint> nodeIds, int position,
        int index)
    {
        int nodeId = tree.NodeIdAt(index);
        return talents.Holds(nodeId) || NamedBefore(nodeIds, position, (uint)nodeId);
    }

    private static bool NamedBefore(IReadOnlyList<uint> nodeIds, int position, uint id)
    {
        for (int i = 0; i < position; i++)
        {
            if (nodeIds[i] == id)
                return true;
        }

        return false;
    }
}
