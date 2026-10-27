using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Abilities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Talents;

/// <summary>A talent node's stat line, as the catalog holds it: a line without a range has its value at both ends.</summary>
public readonly record struct TalentLine(StatType Stat, TalentStatKind Kind, int Value, int ValueMax);

/// <summary>
/// The shared talent tree (#1181), checked whole against the ability catalog of the same generation and immutable
/// afterwards. Built off the tick by the Abilities reload area, after the ability catalog and against it, and published
/// with it (<see cref="Reload.AbilitiesPatch" />), so an ability link is never checked against another generation.
/// <list type="bullet">
/// <item>The nodes sit at dense indices 0..<see cref="Count" /> - 1 in id order. Everything is read by index; a node id is
/// turned into one by <see cref="TryIndexOf" />, a binary search, and nothing is indexed by an id's value.</item>
/// <item>A tree that breaks a rule is refused whole, logged once at Error naming the first problem: it holds no nodes,
/// and keeps only each class's starter whose start row resolves through its node's link to an ability the ability
/// catalog holds, so a bad deploy leaves every character its starter.</item>
/// </list>
/// The rules, in the order they are checked: every class has one start on a Start node, and every Start node is a
/// class's; every edge names nodes the tree holds; exactly the Start and Ability nodes link one ability each, which the
/// ability catalog holds; each fork group holds exactly two capstones; every stat line passes
/// <see cref="TalentStatRules.Problem" />; every notable, ability, capstone and keystone has a name; and every node is
/// reached from the first class's start.
/// </summary>
public sealed class TalentCatalog
{
    private static readonly CharacterClass[] s_classes =
        [CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter, CharacterClass.Cleric];

    private readonly int[] _ids;
    private readonly TalentNodeKind[] _kinds;
    private readonly AbilityId?[] _abilities;
    private readonly int[] _forkPartners;

    // Each node's neighbours and lines, packed: node i's are at [_xStarts[i], _xStarts[i + 1]).
    private readonly int[] _neighbourStarts;
    private readonly int[] _neighbours;
    private readonly int[] _lineStarts;
    private readonly TalentLine[] _lines;

    // One entry per class with a start row that resolves: its start's index (-1 on a refused tree) and its starter.
    private readonly ClassStart[] _starts;

    private TalentCatalog()
    {
        _ids = [];
        _kinds = [];
        _abilities = [];
        _forkPartners = [];
        _neighbourStarts = [0];
        _neighbours = [];
        _lineStarts = [0];
        _lines = [];
        _starts = [];
    }

    public TalentCatalog(TalentTreeRows rows, AbilityCatalog abilities, ILoggerFactory loggerFactory)
        : this()
    {
        ILogger<TalentCatalog> logger = loggerFactory.CreateLogger<TalentCatalog>();

        TalentNode[] nodes = [.. rows.Nodes.OrderBy(n => n.Id)];
        int[] ids = [.. nodes.Select(n => n.Id)];

        string? refusal = Problem(rows, nodes, ids, abilities);
        Tree? tree = null;
        if (refusal is null)
        {
            tree = Tree.Build(rows, nodes, ids, abilities);
            refusal = tree.Disconnected(ids);
        }

        if (refusal is not null || tree is null)
        {
            Refusal = refusal;
            _starts = Starters(rows, nodes, ids, abilities);
            logger.LogError("Refused the talent tree: {Reason}. Characters keep only their starter abilities", Refusal);
            return;
        }

        _ids = ids;
        _kinds = [.. nodes.Select(n => n.Kind)];
        _abilities = tree.Abilities;
        _forkPartners = tree.ForkPartners;
        _neighbourStarts = tree.NeighbourStarts;
        _neighbours = tree.Neighbours;
        _lineStarts = tree.LineStarts;
        _lines = tree.Lines;
        _starts = tree.Starts;
        IsLoaded = true;

        logger.LogInformation("Loaded {Count} talent nodes and {Edges} edges", Count, rows.Edges.Count);
    }

    /// <summary>No tree: before the first load. Holds no node and no starter.</summary>
    public static TalentCatalog Empty { get; } = new();

    /// <summary>True when the tree passed every rule; false for <see cref="Empty" /> and a refused tree.</summary>
    public bool IsLoaded { get; }

    /// <summary>Why the tree was refused, naming the first problem; null when it was not.</summary>
    public string? Refusal { get; }

    /// <summary>The nodes held: 0 on a refused tree.</summary>
    public int Count => _ids.Length;

    /// <summary>The 64-bit words a bit set of every node takes: what <see cref="WalkHeld" />'s sets hold at least.</summary>
    public int BitWords => (Count + 63) / 64;

    /// <summary>The index of the node of that id, or false when the tree does not hold it.</summary>
    public bool TryIndexOf(int nodeId, out int index)
    {
        index = Array.BinarySearch(_ids, nodeId);
        if (index >= 0)
            return true;

        index = -1;
        return false;
    }

    public int NodeIdAt(int index) => _ids[index];

    public TalentNodeKind KindAt(int index) => _kinds[index];

    /// <summary>The indices of the node's neighbours, ascending.</summary>
    public ReadOnlySpan<int> NeighboursAt(int index) =>
        _neighbours.AsSpan(_neighbourStarts[index], _neighbourStarts[index + 1] - _neighbourStarts[index]);

    /// <summary>The ability a Start or an Ability node links; null on every other node.</summary>
    public AbilityId? AbilityAt(int index) => _abilities[index];

    /// <summary>The node's stat lines, by stat then kind.</summary>
    public ReadOnlySpan<TalentLine> LinesAt(int index) =>
        _lines.AsSpan(_lineStarts[index], _lineStarts[index + 1] - _lineStarts[index]);

    /// <summary>The index of the other capstone of the node's fork group, or -1 when it is in none.</summary>
    public int ForkPartnerAt(int index) => _forkPartners[index];

    /// <summary>The index of the class's start, or -1 when the tree holds none (a refused tree, or no such class).</summary>
    public int StartIndexOf(CharacterClass characterClass)
    {
        foreach (ClassStart start in _starts)
        {
            if (start.Class == characterClass)
                return start.Index;
        }

        return -1;
    }

    /// <summary>
    /// The class's starter ability, which its start links; kept on a refused tree when the class's start row resolves
    /// through its Start node's link to an ability the ability catalog holds. Null when it does not.
    /// </summary>
    public AbilityId? StarterOf(CharacterClass characterClass)
    {
        foreach (ClassStart start in _starts)
        {
            if (start.Class == characterClass)
                return start.Starter;
        }

        return null;
    }

    /// <summary>
    /// Sets in <paramref name="reached" /> (cleared first) the start's bit and the bit of every node set in
    /// <paramref name="held" /> that a path of held nodes links to the start. Both sets hold at least
    /// <see cref="BitWords" /> words, bit i of word i / 64 standing for index i, and <paramref name="queue" /> at least
    /// <see cref="Count" /> entries. The caller owns every buffer, so a walk allocates nothing.
    /// </summary>
    public void WalkHeld(int startIndex, ReadOnlySpan<ulong> held, Span<ulong> reached, Span<int> queue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startIndex, Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(held.Length, BitWords, nameof(held));
        ArgumentOutOfRangeException.ThrowIfLessThan(reached.Length, BitWords, nameof(reached));
        ArgumentOutOfRangeException.ThrowIfLessThan(queue.Length, Count, nameof(queue));

        Walk(_neighbourStarts, _neighbours, startIndex, held, reached[..BitWords], queue);
    }

    /// <summary>"305 talent nodes", or "talent tree refused: the reason".</summary>
    public string Describe() => Refusal is null ? $"{Count} talent nodes" : $"talent tree refused: {Refusal}";

    private static void Walk(int[] neighbourStarts, int[] neighbours, int startIndex, ReadOnlySpan<ulong> held,
        Span<ulong> reached, Span<int> queue)
    {
        reached.Clear();
        reached[startIndex >> 6] |= 1UL << startIndex;
        queue[0] = startIndex;
        int head = 0;
        int tail = 1;
        while (head < tail)
        {
            int at = queue[head++];
            for (int i = neighbourStarts[at]; i < neighbourStarts[at + 1]; i++)
            {
                int next = neighbours[i];
                ulong bit = 1UL << next;
                if ((reached[next >> 6] & bit) != 0 || (held[next >> 6] & bit) == 0)
                    continue;

                reached[next >> 6] |= bit;
                queue[tail++] = next;
            }
        }
    }

    private static int IndexOf(int[] ids, int nodeId) => Math.Max(Array.BinarySearch(ids, nodeId), -1);

    /// <summary>The first problem of rules 1-6, or null; rule 7, connection, needs the built tree.</summary>
    private static string? Problem(TalentTreeRows rows, TalentNode[] nodes, int[] ids, AbilityCatalog abilities)
    {
        for (int i = 1; i < ids.Length; i++)
        {
            if (ids[i] == ids[i - 1])
                return $"node {ids[i]} is listed twice";
        }

        return StartProblem(rows, nodes, ids)
               ?? EdgeProblem(rows, ids)
               ?? LinkProblem(rows, nodes, ids, abilities)
               ?? ForkProblem(rows, nodes)
               ?? StatProblem(rows, ids)
               ?? NameProblem(nodes);
    }

    private static string? StartProblem(TalentTreeRows rows, TalentNode[] nodes, int[] ids)
    {
        foreach (CharacterClass characterClass in s_classes)
        {
            int count = rows.Starts.Count(s => s.Class == characterClass);
            if (count != 1)
                return count == 0 ? $"the {characterClass} has no start" : $"the {characterClass} has {count} starts";
        }

        foreach (TalentClassStart start in rows.Starts)
        {
            if (Array.IndexOf(s_classes, start.Class) < 0)
                return $"a start names class {(int)start.Class}, which is no playable class";

            int index = IndexOf(ids, start.NodeId);
            if (index < 0)
                return $"the {start.Class}'s start names node {start.NodeId}, which the tree does not hold";
            if (nodes[index].Kind != TalentNodeKind.Start)
                return $"the {start.Class}'s start is node {start.NodeId}, a {nodes[index].Kind} node, not a Start";
        }

        foreach (TalentNode node in nodes)
        {
            if (node.Kind == TalentNodeKind.Start && !rows.Starts.Any(s => s.NodeId == node.Id))
                return $"Start node {node.Id} is no class's start";
        }

        return null;
    }

    private static string? EdgeProblem(TalentTreeRows rows, int[] ids)
    {
        foreach (TalentEdge edge in rows.Edges)
        {
            if (IndexOf(ids, edge.NodeA) < 0)
                return $"edge {edge.NodeA}-{edge.NodeB} names node {edge.NodeA}, which the tree does not hold";
            if (IndexOf(ids, edge.NodeB) < 0)
                return $"edge {edge.NodeA}-{edge.NodeB} names node {edge.NodeB}, which the tree does not hold";
        }

        return null;
    }

    private static string? LinkProblem(TalentTreeRows rows, TalentNode[] nodes, int[] ids, AbilityCatalog abilities)
    {
        foreach (TalentNodeAbility link in rows.Abilities)
        {
            int index = IndexOf(ids, link.NodeId);
            if (index < 0)
                return $"an ability link names node {link.NodeId}, which the tree does not hold";
            if (!Links(nodes[index].Kind))
                return $"node {link.NodeId} is a {nodes[index].Kind} node and cannot link an ability";
        }

        foreach (TalentNode node in nodes)
        {
            if (!Links(node.Kind))
                continue;

            TalentNodeAbility[] links = [.. rows.Abilities.Where(a => a.NodeId == node.Id)];
            if (links.Length != 1)
            {
                return links.Length == 0
                    ? $"{node.Kind} node {node.Id} links no ability"
                    : $"{node.Kind} node {node.Id} links {links.Length} abilities";
            }

            AbilityId ability = links[0].AbilityId;
            if (abilities.TryGet(ability, out _))
                continue;

            return abilities.Refused.Any(r => r.Id.Value == ability.Value)
                ? $"node {node.Id} links ability {ability.Value}, which the ability catalog refused"
                : $"node {node.Id} links ability {ability.Value}, which the ability catalog does not hold";
        }

        return null;
    }

    private static string? ForkProblem(TalentTreeRows rows, TalentNode[] nodes)
    {
        foreach (TalentNode node in nodes)
        {
            if (node.ForkGroupId is { } group && !rows.ForkGroups.Any(g => g.Id == group))
                return $"node {node.Id} names fork group {group}, which the tree does not hold";
        }

        foreach (TalentForkGroup group in rows.ForkGroups)
        {
            int capstones = nodes.Count(n => n.ForkGroupId == group.Id && n.Kind == TalentNodeKind.Capstone);
            int others = nodes.Count(n => n.ForkGroupId == group.Id && n.Kind != TalentNodeKind.Capstone);
            if (capstones != 2 || others != 0)
            {
                return $"fork group {group.Id} holds {capstones} capstones and {others} other nodes, " +
                       "not exactly two capstones";
            }
        }

        return null;
    }

    private static string? StatProblem(TalentTreeRows rows, int[] ids)
    {
        foreach (TalentNodeStat line in rows.Stats)
        {
            if (IndexOf(ids, line.NodeId) < 0)
                return $"a stat line names node {line.NodeId}, which the tree does not hold";
            if (TalentStatRules.Problem(line.Stat, line.Kind, line.Value, line.ValueMax) is { } problem)
                return $"node {line.NodeId}'s stat line: {problem}";
        }

        return null;
    }

    private static string? NameProblem(TalentNode[] nodes)
    {
        foreach (TalentNode node in nodes)
        {
            bool named = node.Kind is TalentNodeKind.Notable or TalentNodeKind.Ability or TalentNodeKind.Capstone
                or TalentNodeKind.Keystone;
            if (named && string.IsNullOrWhiteSpace(node.Name))
                return $"{node.Kind} node {node.Id} has no name";
        }

        return null;
    }

    private static bool Links(TalentNodeKind kind) => kind is TalentNodeKind.Start or TalentNodeKind.Ability;

    /// <summary>
    /// A refused tree's starters: each class whose start row names a Start node, linked to an ability the ability catalog
    /// holds. Its start's index is -1, since a refused tree holds no node.
    /// </summary>
    private static ClassStart[] Starters(TalentTreeRows rows, TalentNode[] nodes, int[] ids, AbilityCatalog abilities)
    {
        List<ClassStart> starts = [];
        foreach (CharacterClass characterClass in s_classes)
        {
            if (rows.Starts.FirstOrDefault(s => s.Class == characterClass) is not { } start)
                continue;

            int index = IndexOf(ids, start.NodeId);
            if (index < 0 || nodes[index].Kind != TalentNodeKind.Start)
                continue;

            if (rows.Abilities.FirstOrDefault(a => a.NodeId == start.NodeId) is { } link
                && abilities.TryGet(link.AbilityId, out _))
            {
                starts.Add(new ClassStart(characterClass, -1, link.AbilityId));
            }
        }

        return [.. starts];
    }

    private readonly record struct ClassStart(CharacterClass Class, int Index, AbilityId? Starter);

    /// <summary>A tree that passed rules 1-6, laid out at dense indices; rule 7 is checked on it.</summary>
    private sealed class Tree
    {
        public required AbilityId?[] Abilities { get; init; }
        public required int[] ForkPartners { get; init; }
        public required int[] NeighbourStarts { get; init; }
        public required int[] Neighbours { get; init; }
        public required int[] LineStarts { get; init; }
        public required TalentLine[] Lines { get; init; }
        public required ClassStart[] Starts { get; init; }

        public static Tree Build(TalentTreeRows rows, TalentNode[] nodes, int[] ids, AbilityCatalog abilities)
        {
            int count = ids.Length;

            var linked = new AbilityId?[count];
            foreach (TalentNodeAbility link in rows.Abilities)
                linked[IndexOf(ids, link.NodeId)] = link.AbilityId;

            int[] partners = new int[count];
            Array.Fill(partners, -1);
            foreach (TalentForkGroup group in rows.ForkGroups)
            {
                int[] pair = [.. Enumerable.Range(0, count).Where(i => nodes[i].ForkGroupId == group.Id)];
                partners[pair[0]] = pair[1];
                partners[pair[1]] = pair[0];
            }

            List<int>[] adjacent = [.. Enumerable.Range(0, count).Select(_ => new List<int>())];
            foreach (TalentEdge edge in rows.Edges)
            {
                int a = IndexOf(ids, edge.NodeA);
                int b = IndexOf(ids, edge.NodeB);
                adjacent[a].Add(b);
                adjacent[b].Add(a);
            }

            int[] neighbourStarts = new int[count + 1];
            List<int> neighbours = [];
            for (int i = 0; i < count; i++)
            {
                neighbours.AddRange(adjacent[i].Distinct().Order());
                neighbourStarts[i + 1] = neighbours.Count;
            }

            ILookup<int, TalentNodeStat> byNode = rows.Stats.ToLookup(s => IndexOf(ids, s.NodeId));
            int[] lineStarts = new int[count + 1];
            List<TalentLine> lines = [];
            for (int i = 0; i < count; i++)
            {
                lines.AddRange(byNode[i]
                    .OrderBy(s => s.Stat).ThenBy(s => s.Kind)
                    .Select(s => new TalentLine(s.Stat, s.Kind, s.Value, s.ValueMax ?? s.Value)));
                lineStarts[i + 1] = lines.Count;
            }

            ClassStart[] starts =
            [
                .. s_classes.Select(c =>
                {
                    int index = IndexOf(ids, rows.Starts.Single(s => s.Class == c).NodeId);
                    return new ClassStart(c, index, linked[index]);
                }),
            ];

            return new Tree
            {
                Abilities = linked,
                ForkPartners = partners,
                NeighbourStarts = neighbourStarts,
                Neighbours = [.. neighbours],
                LineStarts = lineStarts,
                Lines = [.. lines],
                Starts = starts,
            };
        }

        /// <summary>Rule 7: the lowest id no walk from the first class's start reaches, over every node, or null.</summary>
        public string? Disconnected(int[] ids)
        {
            int count = ids.Length;
            int words = (count + 63) / 64;
            ulong[] all = new ulong[words];
            Array.Fill(all, ulong.MaxValue);
            ulong[] reached = new ulong[words];
            Walk(NeighbourStarts, Neighbours, Starts[0].Index, all, reached, new int[count]);

            for (int i = 0; i < count; i++)
            {
                if ((reached[i >> 6] & (1UL << i)) == 0)
                    return $"node {ids[i]} is not reached from the {Starts[0].Class}'s start";
            }

            return null;
        }
    }
}
