using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Talents;

namespace Avalon.Balance.Core;

/// <summary>An ability a build holds (B-R13), and the shape its held capstones give it: none when no capstone shapes it.</summary>
public sealed record HeldAbility(AbilityTemplate Template, AbilityShaping Shaping);

/// <summary>A node of the tree as the seed names it: its kind, and its name, null on a node the seed leaves unnamed.</summary>
public sealed record TalentNodeInfo(int Id, TalentNodeKind Kind, string? Name);

/// <summary>
/// The talent nodes one build holds at a level (B-R10), and what they give it: the stat lines and keystone rules it folds,
/// and its abilities with their shapes. Immutable once built: nothing adds to its <see cref="Totals" /> after
/// <see cref="TalentTree.Hold" />, and each instance has totals of its own, so no two rows share one.
/// </summary>
public sealed class HeldTalents
{
    internal HeldTalents(CharacterClass characterClass, int[] nodes, TalentTotals totals, HeldAbility[] abilities)
    {
        Class = characterClass;
        Nodes = Array.AsReadOnly(nodes);
        Totals = totals;
        Abilities = Array.AsReadOnly(abilities);
    }

    public CharacterClass Class { get; }

    /// <summary>The nodes held, in the order they were taken.</summary>
    public IReadOnlyList<int> Nodes { get; }

    /// <summary>
    /// The held nodes' stat lines and keystone rules (B-R12), added as the world's TalentAllocation.RebuildTotals adds
    /// them: nodes by ascending id, each node's lines by stat then kind, then its keystone rule, so every More multiplies
    /// in the world's order. <see cref="TalentTotals.IsEmpty" /> when no held node carries a line or a rule. Read it, never
    /// write it: each <see cref="TalentTree.Hold" /> builds a fresh instance, which every reader of this build shares.
    /// </summary>
    public TalentTotals Totals { get; }

    /// <summary>
    /// The abilities held (B-R13), as the world's TalentAbilities.Rebuild orders them: the class start's starter, then each
    /// held Ability node's ability by ascending node id; each shaped by the held capstones whose modifiers name it.
    /// </summary>
    public IReadOnlyList<HeldAbility> Abilities { get; }

    public bool Holds(uint abilityId)
    {
        foreach (HeldAbility ability in Abilities)
        {
            if (ability.Template.Id.Value == abilityId)
                return true;
        }

        return false;
    }
}

/// <summary>
/// The shared talent tree (#1181) as the simulator reads it from the seed, checked as the world's TalentCatalog checks
/// the rows an override can reach (B-R11): every stat line (<see cref="TalentStatRules" />), every ability link, every
/// keystone rule and every capstone modifier (<see cref="TalentModifierRules" />) against the ability and aura catalogs.
/// The tree's structure (ids, edges, kinds, forks, starts) is the seed's, held by SeedIntegrityShould. A build is held
/// with the world's allocation rules, mirrored from TalentRules.Allocate (World-side), in <see cref="Hold" />.
/// Immutable once built, so one tree serves every row of a run.
/// </summary>
public sealed class TalentTree
{
    private readonly IReadOnlyDictionary<int, Node> _nodes;
    private readonly IReadOnlyDictionary<CharacterClass, int> _starts;
    private readonly IReadOnlyDictionary<AbilityId, AbilityTemplate> _abilities;

    // For each ability a capstone shapes, the ids of the capstones whose modifiers name it, ascending.
    private readonly IReadOnlyDictionary<uint, int[]> _shapers;

    private TalentTree(IReadOnlyDictionary<int, Node> nodes, IReadOnlyDictionary<CharacterClass, int> starts,
        IReadOnlyDictionary<AbilityId, AbilityTemplate> abilities, IReadOnlyDictionary<uint, int[]> shapers)
    {
        _nodes = nodes;
        _starts = starts;
        _abilities = abilities;
        _shapers = shapers;
    }

    /// <param name="abilities">The ability catalog, as <see cref="BalanceData.From" /> checked it.</param>
    /// <param name="auras">The aura catalog the abilities were checked against.</param>
    /// <exception cref="InvalidDataException">A row the world's talent catalog would refuse; the message names the first.</exception>
    public static TalentTree From(SeedTables tables, IReadOnlyDictionary<AbilityId, AbilityTemplate> abilities,
        IReadOnlyDictionary<AuraId, AuraTemplate> auras)
    {
        if (Problem(tables, abilities, auras) is { } problem)
            throw new InvalidDataException("The talent tree refused: " + problem);

        ILookup<int, int> neighbours = tables.TalentEdges
            .SelectMany(e => new[] { (From: e.NodeA, To: e.NodeB), (From: e.NodeB, To: e.NodeA) })
            .ToLookup(e => e.From, e => e.To);
        ILookup<int, TalentNodeStat> lines = tables.TalentNodeStats.ToLookup(s => s.NodeId);
        ILookup<int, TalentAbilityModifier> modifiers = tables.TalentAbilityModifiers.ToLookup(m => m.NodeId);
        var links = tables.TalentNodeAbilities.ToDictionary(l => l.NodeId, l => l.AbilityId);
        var partners = new Dictionary<int, int>();
        foreach (IGrouping<int, TalentNode> fork in tables.TalentNodes
                     .Where(n => n.ForkGroupId is not null)
                     .GroupBy(n => n.ForkGroupId!.Value))
        {
            TalentNode[] pair = [.. fork];
            if (pair.Length != 2)
                throw new InvalidDataException($"The talent tree refused: fork group {fork.Key} holds {pair.Length} nodes, not two");

            partners[pair[0].Id] = pair[1].Id;
            partners[pair[1].Id] = pair[0].Id;
        }

        var nodes = tables.TalentNodes.ToDictionary(n => n.Id, n => new Node(
            n.Kind,
            n.Name,
            [.. neighbours[n.Id].Distinct()],
            partners.TryGetValue(n.Id, out int partner) ? partner : null,
            [.. lines[n.Id].OrderBy(s => s.Stat).ThenBy(s => s.Kind)
                .Select(s => new Line(s.Stat, s.Kind, s.Value, s.ValueMax ?? s.Value))],
            n.Keystone,
            links.TryGetValue(n.Id, out AbilityId? ability) ? ability : null,
            [.. modifiers[n.Id].OrderBy(m => m.Kind).Select(m => new Modifier(m.Kind, m.Value, m.Value2))]));

        var shapers = tables.TalentAbilityModifiers
            .GroupBy(m => m.AbilityId.Value)
            .ToDictionary(g => g.Key, g => g.Select(m => m.NodeId).Distinct().Order().ToArray());

        var starts = tables.TalentClassStarts.ToDictionary(s => s.Class, s => s.NodeId);
        return new TalentTree(nodes, starts, abilities, shapers);
    }

    /// <summary>The node <paramref name="id" />, as the seed names it.</summary>
    /// <exception cref="InvalidDataException">The tree holds no node <paramref name="id" />.</exception>
    public TalentNodeInfo Describe(int id) => _nodes.TryGetValue(id, out Node? node)
        ? new TalentNodeInfo(id, node.Kind, node.Name)
        : throw new InvalidDataException($"The talent tree holds no node {id}");

    /// <summary>What a new character of the class holds: no node, its starter alone.</summary>
    /// <exception cref="InvalidDataException">The tree has no start for the class.</exception>
    public HeldTalents Starter(CharacterClass characterClass) => Hold(characterClass, []);

    /// <summary>
    /// The build that takes <paramref name="nodes" /> in order, each checked as the world's TalentRules.Allocate checks a
    /// node against the ones before it (B-R11): <see cref="TalentResult.UnknownNode" /> when the tree does not hold it,
    /// <see cref="TalentResult.AlreadyAllocated" /> when it was taken earlier or is a start,
    /// <see cref="TalentResult.NotConnected" /> when no neighbour is the class's start or a node taken earlier, and
    /// <see cref="TalentResult.ForkLocked" /> when its fork partner was taken earlier. Points are not counted: the caller
    /// takes the prefix a level earns, and every prefix of a path that passes passes too.
    /// </summary>
    /// <exception cref="InvalidDataException">"node {id}: {Reason}" for the first node refused, or no start for the class.</exception>
    public HeldTalents Hold(CharacterClass characterClass, IReadOnlyList<int> nodes)
    {
        if (!_starts.TryGetValue(characterClass, out int start) || !_nodes.TryGetValue(start, out Node? startNode))
            throw new InvalidDataException($"The talent tree has no start for {characterClass}");

        int[] taken = [.. nodes];
        var held = new HashSet<int>();
        foreach (int id in taken)
        {
            if (Refusal(id, start, held) is { } reason)
                throw new InvalidDataException($"node {id}: {reason}");

            held.Add(id);
        }

        int[] ascending = [.. taken.Order()];
        var totals = new TalentTotals();
        AbilityId starter = startNode.Ability
            ?? throw new InvalidDataException($"{characterClass}'s start {start} links no ability");
        List<HeldAbility> abilities = [Held(starter, held)];
        foreach (int id in ascending)
        {
            Node node = _nodes[id];
            foreach (Line line in node.Lines)
                totals.Add(line.Stat, line.Kind, line.Value, line.ValueMax);

            if (node.Keystone is { } rule)
                totals.AddKeystone(rule);

            if (node.Kind == TalentNodeKind.Ability && node.Ability is { } ability)
                abilities.Add(Held(ability, held));
        }

        return new HeldTalents(characterClass, taken, totals, [.. abilities]);
    }

    private TalentResult? Refusal(int id, int start, HashSet<int> held)
    {
        if (!_nodes.TryGetValue(id, out Node? node))
            return TalentResult.UnknownNode;
        if (held.Contains(id) || node.Kind == TalentNodeKind.Start)
            return TalentResult.AlreadyAllocated;
        if (!node.Neighbours.Any(n => n == start || held.Contains(n)))
            return TalentResult.NotConnected;
        if (node.ForkPartner is { } partner && held.Contains(partner))
            return TalentResult.ForkLocked;

        return null;
    }

    /// <summary>The ability, shaped by the modifiers of every held capstone that names it, summed (TalentAbilities.ShapingOf).</summary>
    private HeldAbility Held(AbilityId id, HashSet<int> held)
    {
        AbilityShaping shaping = default;
        if (_shapers.TryGetValue(id.Value, out int[]? capstones))
        {
            foreach (int capstone in capstones)
            {
                if (!held.Contains(capstone))
                    continue;

                foreach (Modifier modifier in _nodes[capstone].Modifiers)
                    shaping.Add(modifier.Kind, modifier.Value, modifier.Value2);
            }
        }

        return new HeldAbility(_abilities[id], shaping);
    }

    /// <summary>
    /// The first problem of the rows an override can reach, in the world's catalog's order (links, stat lines, keystones,
    /// capstone modifiers), or null.
    /// </summary>
    private static string? Problem(SeedTables tables, IReadOnlyDictionary<AbilityId, AbilityTemplate> abilities,
        IReadOnlyDictionary<AuraId, AuraTemplate> auras)
    {
        foreach (TalentNodeAbility link in tables.TalentNodeAbilities)
        {
            if (!abilities.ContainsKey(link.AbilityId))
                return $"node {link.NodeId} links ability {link.AbilityId.Value}, which the ability catalog does not hold";
        }

        foreach (TalentNodeStat line in tables.TalentNodeStats)
        {
            if (TalentStatRules.Problem(line.Stat, line.Kind, line.Value, line.ValueMax) is { } problem)
                return $"node {line.NodeId}'s {line.Stat} {line.Kind} line: {problem}";
        }

        foreach (TalentNode node in tables.TalentNodes)
        {
            if (node.Keystone is not { } rule)
                continue;
            if (node.Kind != TalentNodeKind.Keystone)
                return $"node {node.Id} is a {node.Kind} node and cannot carry a keystone rule";
            if (!Enum.IsDefined(rule))
                return $"node {node.Id} names keystone rule {(int)rule}, which is not one";
        }

        foreach (TalentAbilityModifier modifier in tables.TalentAbilityModifiers)
        {
            if (!abilities.TryGetValue(modifier.AbilityId, out AbilityTemplate? template))
                return $"node {modifier.NodeId} shapes ability {modifier.AbilityId.Value}, which the ability catalog does not hold";

            AuraTemplate? aura = template.AuraId is { } auraId && auras.TryGetValue(auraId, out AuraTemplate? held) ? held : null;
            if (TalentModifierRules.RowProblem(modifier, tables.TalentAbilityModifiers, template, aura) is { } problem)
                return problem;
        }

        return null;
    }

    private sealed record Node(
        TalentNodeKind Kind,
        string? Name,
        int[] Neighbours,
        int? ForkPartner,
        Line[] Lines,
        KeystoneRule? Keystone,
        AbilityId? Ability,
        Modifier[] Modifiers);

    private readonly record struct Line(StatType Stat, TalentStatKind Kind, int Value, int ValueMax);

    private readonly record struct Modifier(TalentModifierKind Kind, int Value, int? Value2);
}
