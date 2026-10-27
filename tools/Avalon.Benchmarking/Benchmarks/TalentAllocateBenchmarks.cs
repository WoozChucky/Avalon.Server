using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Talents;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Talents;
using Avalon.World.Testing.Fakes;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What a talent allocation costs (#1181), over the seeded tree and reference data. The decision rows run
/// <see cref="TalentRules.Allocate" /> for a level-10 Warrior holding nothing, so 10 points are left:
/// <see cref="Decide_refused" /> (the baseline) names five nodes down lane A and is refused NotConnected at the fifth
/// (WH-A7, whose neighbours are neither held nor named); <see cref="Decide_accepted" /> names the lane's first five
/// (WH-A1 to WH-A5) and is accepted; <see cref="Decide_refused_long" /> is the longest request the rules scan: 135
/// points earned (the design's total at level 100), 134 nodes in walk order from the Warrior's start and then one that
/// touches none of them, refused NotConnected at the last id after every neighbour of it was scanned against all 134
/// named before it (the path that grows as n squared times the degree). The change rows run
/// <see cref="TalentChange.Apply" /> for a level-15 Warrior holding 18 nodes (WH-A1 to WH-A10, WH-B1 to WH-B8, Cleave
/// at WH-A5): <see cref="Apply_small_nodes" /> sums them again, finds the abilities unchanged and refreshes the stats;
/// <see cref="Apply_ability_change" /> drops WH-A5 and allocates it again through <see cref="TalentAllocation" />, each
/// followed by the change, so Cleave leaves, comes back as a new ability and the ability list is sent twice. 0 B expected
/// on every row but <see cref="Apply_ability_change" />, whose bytes are the two rebuilt ability lists, Cleave's new
/// ability and the two SMSG_CHARACTER_ABILITIES sends (their arrays and packets).
/// </summary>
/// <remarks>
/// Neither the rules nor the change read an instance or the handler's packet, so the rows call them directly, as the
/// handler does, with a <see cref="QuietConnection" /> counting the ability lists sent. Setup fails the run unless each
/// decision row's first call gives its intended result (and names the node it should), unless
/// <see cref="Apply_small_nodes" /> sends nothing, and unless <see cref="Apply_ability_change" /> sends two ability
/// lists and ends holding Cleave again.
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
    private static readonly AbilityId s_cleaveAbility = new(200);

    private StaticData _data = null!;
    private TalentCatalog _tree = null!;
    private CharacterEntity _fresh = null!;
    private int _freshEarned;
    private CharacterEntity _holder = null!;
    private QuietConnection _connection = null!;
    private uint[] _long = null!;

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

        Apply_small_nodes();
        if (_connection.Sent != 0)
            throw new InvalidOperationException($"A change of small nodes sent {_connection.Sent} ability lists, not none.");

        Apply_ability_change();
        if (_connection.Sent != 2 || _holder.Spells[s_cleaveAbility] is null || _holder.Talents.Spent != s_eighteen.Length)
        {
            throw new InvalidOperationException(
                $"Dropping and allocating Cleave sent {_connection.Sent} ability lists, not 2, or left it unheld.");
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
        TalentChange.Apply(_connection, _holder, _data, NullLogger.Instance);
        return _connection.Sent;
    }

    [Benchmark]
    public int Apply_ability_change()
    {
        _holder.Talents.Drop((int)s_cleave[0]);
        TalentChange.Apply(_connection, _holder, _data, NullLogger.Instance);
        _holder.Talents.Allocate(s_cleave);
        TalentChange.Apply(_connection, _holder, _data, NullLogger.Instance);
        return _connection.Sent;
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
    /// 134 nodes in breadth-first order from the Warrior's start, never through a start, so each touches the start or one
    /// named before it; then the first node by id that touches neither the start nor any of them.
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
                if (seen[neighbour] || _tree.KindAt(neighbour) == TalentNodeKind.Start)
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

    private TalentResult Decide(uint[] request, int earned, out uint refused) =>
        TalentRules.Allocate(_fresh.Talents, _tree, _fresh.Class, _fresh.IsDead, earned, request, out refused);

    private void Expect(uint[] request, int earned, TalentResult intended, uint intendedNode)
    {
        TalentResult answered = Decide(request, earned, out uint refused);
        if (answered != intended || refused != intendedNode)
        {
            throw new InvalidOperationException(
                $"A request of {request.Length} nodes was answered {answered} naming {refused}, not {intended} naming {intendedNode}.");
        }
    }
}
