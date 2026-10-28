using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Talents;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Talents;
using Avalon.World.Testing.Fakes;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What a talent allocation and a refund cost (#1181), over the seeded tree and reference data. The decision rows run
/// <see cref="TalentRules.Allocate" /> for a level-10 Warrior holding nothing, so 10 points are left:
/// <see cref="Decide_refused" /> (the baseline) names five nodes down lane A and is refused NotConnected at the fifth
/// (WH-A7, whose neighbours are neither held nor named); <see cref="Decide_accepted" /> names the lane's first five
/// (WH-A1 to WH-A5) and is accepted; <see cref="Decide_refused_long" /> is the longest request the rules check: 135
/// points earned (the design's total at level 100), 134 nodes in walk order from the Warrior's start and then one that
/// touches none of them, refused NotConnected at the last id, after each id's neighbours were tested against the held
/// nodes and the bits of the ids named before it (the path that grows as n times the degree). The refund rows run
/// <see cref="TalentRules.Refund" /> (Part C): <see cref="Refund_refused" /> asks a level-15 Warrior holding 18 nodes
/// (WH-A1 to WH-A10, WH-B1 to WH-B8) for both lanes' first nodes (WH-A1, WH-B1) and is refused WouldDisconnect naming
/// WH-A2, the lowest node it would strand; <see cref="Refund_accepted" /> asks it for the two lane tips (WH-A10, WH-B8)
/// and is accepted; <see cref="Refund_refused_long" /> asks a Warrior holding the 134 nodes of the long request for
/// every held neighbour of its start, refused WouldDisconnect after both walks (the first over all 134 nodes). The
/// change rows run <see cref="TalentChange.Apply" /> for the 18-node Warrior (Cleave at WH-A5):
/// <see cref="Apply_small_nodes" /> sums them again, finds the abilities unchanged and refreshes the stats;
/// <see cref="Apply_ability_change" /> drops WH-A5 and allocates it again through <see cref="TalentAllocation" />, each
/// followed by the change, so Cleave is parked and taken back, and the ability list is owed, not sent;
/// <see cref="Cycle_flushed" /> is that change followed by the tick's <see cref="TalentFlusher.Flush" />, which sends
/// the owed list and state once. <see cref="Apply_capstone_change" /> (Part D) is a level-15 Warrior holding Cleave and
/// its branch (WH-A1 to WH-A5, WH-A5.c1 to WH-A5.c5) taking Wide Arc (306) and dropping it again, each followed by the
/// change: Cleave stays held and swaps between its unshaped metadata and the shaped copy it keeps, and each change owes
/// the ability list. 0 B expected on every row: the packets are the encoder's scratch, the list's entries and amounts
/// are the last list's, overwritten, since the change leaves as many abilities held, and a shape already seen is the
/// copy Cleave keeps.
/// </summary>
/// <remarks>
/// Neither the rules, the change nor the flusher read an instance or the handler's packet, so the rows call them
/// directly, as the handlers and the tick do, with a <see cref="QuietConnection" /> counting the packets sent. Setup
/// fails the run unless each decision row's first call gives its intended result (and names the node it should),
/// unless <see cref="Apply_small_nodes" /> sends nothing, unless <see cref="Apply_ability_change" /> sends nothing (the
/// ability list waits for the tick's TalentFlusher) and ends holding Cleave again, unless
/// <see cref="Cycle_flushed" /> sends exactly the list and the state, and unless <see cref="Apply_capstone_change" />
/// owes one ability list after each change and ends with Cleave's arc back at 100.
/// </remarks>
[MemoryDiagnoser]
public class TalentAllocateBenchmarks
{
    private const int LongEarned = 135;

    // The seed's ids (labels are source-only keys): WH-A1..WH-A10 are nodes 2-11, WH-B1..WH-B8 nodes 12-19.
    private static readonly uint[] s_accepted = [2, 3, 4, 5, 6];
    private static readonly uint[] s_refused = [2, 3, 4, 5, 8];
    private static readonly uint[] s_cleave = [6];
    private static readonly int[] s_eighteen = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19];
    private static readonly uint[] s_refundRefused = [2, 12];
    private static readonly uint[] s_refundAccepted = [11, 19];
    private static readonly AbilityId s_cleaveAbility = new(200);

    // Cleave's branch: WH-A1..WH-A5 (2-6) and WH-A5.c1..WH-A5.c5 (22-26); Wide Arc (WH-A5.cx) is node 306.
    private static readonly int[] s_cleaveBranch = [2, 3, 4, 5, 6, 22, 23, 24, 25, 26];
    private const int WideArc = 306;
    private static readonly uint[] s_wideArc = [WideArc];

    private StaticData _data = null!;
    private TalentCatalog _tree = null!;
    private CharacterEntity _fresh = null!;
    private int _freshEarned;
    private CharacterEntity _holder = null!;
    private QuietConnection _connection = null!;
    private uint[] _long = null!;
    private CharacterEntity _long134 = null!;
    private CharacterEntity _capstoner = null!;
    private uint[] _startNeighbours = null!;
    private readonly TalentConfiguration _config = new();

    [GlobalSetup]
    public void Setup()
    {
        _data = ScenarioReferenceData.Shared.Data;
        _tree = _data.Talents;
        if (!_tree.IsLoaded)
            throw new InvalidOperationException($"The seeded tree did not load: {_tree.Refusal}");

        _fresh = Warrior(1, level: 10, held: []);
        _freshEarned = TalentPoints.Earned(_fresh.Level, _fresh.Quests, _data.Quests);
        _holder = Warrior(2, level: 15, held: s_eighteen);
        _connection = new QuietConnection(_holder);
        _long = LongRequest();

        Expect(s_refused, _freshEarned, TalentResult.NotConnected, s_refused[^1]);
        Expect(s_accepted, _freshEarned, TalentResult.Ok, 0);
        Expect(_long, LongEarned, TalentResult.NotConnected, _long[^1]);

        // The refund rules read no level or points, so the long holder takes the 18-node Warrior's level.
        int[] long134 = [.. _long[..^1].Select(id => (int)id)];
        _long134 = Warrior(3, level: 15, held: long134);
        _startNeighbours = [.. StartNeighbours().Where(id => _long134.Talents.Holds((int)id))];
        uint lowestLeft = (uint)long134.Where(id => Array.IndexOf(_startNeighbours, (uint)id) < 0).Min();
        ExpectRefund(_holder, s_refundRefused, TalentResult.WouldDisconnect, 3);
        ExpectRefund(_holder, s_refundAccepted, TalentResult.Ok, 0);
        ExpectRefund(_long134, _startNeighbours, TalentResult.WouldDisconnect, lowestLeft);

        Apply_small_nodes();
        if (_connection.Sent != 0)
            throw new InvalidOperationException($"A change of small nodes sent {_connection.Sent} ability lists, not none.");

        Apply_ability_change();
        if (_connection.Sent != 0 || _holder.Spells[s_cleaveAbility] is null
            || _holder.Talents.Spent != s_eighteen.Length)
        {
            throw new InvalidOperationException(
                $"Dropping and allocating Cleave sent {_connection.Sent} ability lists, not none, or left it unheld.");
        }

        int flushed = Cycle_flushed();
        if (flushed != 2)
        {
            throw new InvalidOperationException(
                $"A flushed change sent {flushed} packets, not the list and the state.");
        }

        _capstoner = Warrior(4, level: 15, held: s_cleaveBranch);
        int owed = Apply_capstone_change();
        float arc = _capstoner.Spells[s_cleaveAbility]?.Metadata.ArcDegrees ?? 0f;
        if (owed != 2 || arc != 100f || _capstoner.Talents.Holds(WideArc))
        {
            throw new InvalidOperationException($"Taking and dropping Wide Arc owed {owed} ability lists, not one " +
                $"after each change, or left Cleave's arc at {arc}, not 100.");
        }
    }

    [Benchmark(Baseline = true)]
    public TalentResult Decide_refused() => Decide(s_refused, _freshEarned, out _);

    [Benchmark]
    public TalentResult Decide_accepted() => Decide(s_accepted, _freshEarned, out _);

    [Benchmark]
    public TalentResult Decide_refused_long() => Decide(_long, LongEarned, out _);

    [Benchmark]
    public int Apply_small_nodes()
    {
        TalentChange.Apply(_holder, _data, NullLogger.Instance);
        return _connection.Sent;
    }

    [Benchmark]
    public int Apply_ability_change()
    {
        _holder.Talents.Drop((int)s_cleave[0]);
        TalentChange.Apply(_holder, _data, NullLogger.Instance);
        _holder.Talents.Allocate(s_cleave);
        TalentChange.Apply(_holder, _data, NullLogger.Instance);
        return _connection.Sent;
    }

    /// <summary>
    /// A capstone taken and dropped, each followed by the change: the changes that owed the ability list (2).
    /// </summary>
    [Benchmark]
    public int Apply_capstone_change()
    {
        _capstoner.Talents.Allocate(s_wideArc);
        int owed = Change(_capstoner);
        _capstoner.Talents.Drop(WideArc);
        return owed + Change(_capstoner);
    }

    [Benchmark]
    public TalentResult Refund_refused() => Refund(_holder, s_refundRefused, out _);

    [Benchmark]
    public TalentResult Refund_accepted() => Refund(_holder, s_refundAccepted, out _);

    [Benchmark]
    public TalentResult Refund_refused_long() => Refund(_long134, _startNeighbours, out _);

    /// <summary>
    /// A change of the abilities held, then the tick's flush: the packets sent (the list and the state).
    /// </summary>
    [Benchmark]
    public int Cycle_flushed()
    {
        int before = _connection.Sent;
        Apply_ability_change();
        TalentFlusher.Flush(_connection, _data.Quests, _config);
        return _connection.Sent - before;
    }

    /// <summary>
    /// <see cref="TalentChange.Apply" /> with nothing owed before it, as after the tick's flush: 1 when it owes the
    /// ability list.
    /// </summary>
    private int Change(CharacterEntity character)
    {
        character.Talents.AbilitiesSent();
        TalentChange.Apply(character, _data, NullLogger.Instance);
        return character.Talents.AbilitiesOwed ? 1 : 0;
    }

    /// <summary>A Warrior entering the world holding <paramref name="held" />, as character select leaves it.</summary>
    private CharacterEntity Warrior(uint id, ushort level, int[] held)
    {
        var row = new Character
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(id),
            Name = $"Talents{id}",
            Class = CharacterClass.Warrior,
            Level = level,
            CreationDate = DateTime.UtcNow,
        };
        var character = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration()) { Data = row };
        character.Talents.Load([.. held.Select(node => new CharacterTalent { CharacterId = row.Id, NodeId = node })]);
        character.Talents.RebuildTotals(_tree);
        if (!CharacterStatsRefresh.Apply(character, _data, CurrentValues.EnterWorld))
            throw new InvalidOperationException($"The reference data has no Warrior row at level {level}.");

        TalentAbilities.Rebuild(character, _tree, _data.Abilities, NullLogger.Instance);
        character.Talents.StateSent(level);
        return character;
    }

    /// <summary>
    /// 134 nodes in breadth-first order from the Warrior's start, never through a start nor to a capstone whose fork
    /// partner was taken (Part D), so each touches the start or one named before it and none is ForkLocked; then the first
    /// node by id that touches neither the start nor any of them.
    /// </summary>
    private uint[] LongRequest()
    {
        int start = _tree.StartIndexOf(CharacterClass.Warrior);
        bool[] seen = new bool[_tree.Count];
        var order = new List<int>();
        var queue = new Queue<int>();
        seen[start] = true;
        queue.Enqueue(start);
        while (queue.Count > 0 && order.Count < LongEarned - 1)
        {
            foreach (int neighbour in _tree.NeighboursAt(queue.Dequeue()))
            {
                int partner = _tree.ForkPartnerAt(neighbour);
                if (seen[neighbour] || _tree.KindAt(neighbour) == TalentNodeKind.Start || (partner >= 0 && seen[partner]))
                    continue;

                seen[neighbour] = true;
                queue.Enqueue(neighbour);
                order.Add(neighbour);
                if (order.Count == LongEarned - 1)
                    break;
            }
        }

        if (order.Count != LongEarned - 1)
            throw new InvalidOperationException($"The Warrior's start reaches {order.Count} nodes, not {LongEarned - 1}.");

        int last = Enumerable.Range(0, _tree.Count).First(index => !seen[index]
            && _tree.KindAt(index) != TalentNodeKind.Start
            && !_tree.NeighboursAt(index).ToArray().Any(neighbour => seen[neighbour]));
        return [.. order.Select(index => (uint)_tree.NodeIdAt(index)), (uint)_tree.NodeIdAt(last)];
    }

    /// <summary>The ids of the Warrior start's neighbours.</summary>
    private IEnumerable<uint> StartNeighbours() => _tree.NeighboursAt(_tree.StartIndexOf(CharacterClass.Warrior))
        .ToArray()
        .Select(index => (uint)_tree.NodeIdAt(index));

    private TalentResult Refund(CharacterEntity character, uint[] request, out uint refused) =>
        TalentRules.Refund(character.Talents, _tree, character.Class, request, out refused);

    private void ExpectRefund(CharacterEntity character, uint[] request, TalentResult intended, uint intendedNode)
    {
        TalentResult answered = Refund(character, request, out uint refused);
        if (answered != intended || refused != intendedNode)
        {
            throw new InvalidOperationException($"A refund of {request.Length} nodes was answered {answered} naming " +
                $"{refused}, not {intended} naming {intendedNode}.");
        }
    }

    private TalentResult Decide(uint[] request, int earned, out uint refused) =>
        TalentRules.Allocate(_fresh.Talents, _tree, _fresh.Class, _fresh.IsDead, earned, request, out refused);

    private void Expect(uint[] request, int earned, TalentResult intended, uint intendedNode)
    {
        TalentResult answered = Decide(request, earned, out uint refused);
        if (answered != intended || refused != intendedNode)
        {
            throw new InvalidOperationException($"A request of {request.Length} nodes was answered {answered} naming " +
                $"{refused}, not {intended} naming {intendedNode}.");
        }
    }
}
