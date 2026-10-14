using Avalon.Common;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Enums;
using Avalon.World.Instances;
using Avalon.World.Quests;
using Avalon.World.Testing.Fakes;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The tick's quest flush (#977): <c>QuestFlusher.Flush</c> over <see cref="Connections" /> connections, as WorldServer
/// runs it for every connection every tick, through the real <see cref="QuestService" /> over the World server's
/// reference data (the seeded quests) and a real town instance. Each character holds the seeded quest 1 (a kill
/// objective), has had its log sent and has flushed once, so what is left is the steady flush. <see cref="NoChange" />:
/// nothing changed since the last flush, the common tick. <see cref="OneChanged" />: one character's kill count moved,
/// so it is owed an update and its markers are worked out again. <see cref="AllChanged" />: every character's moved.
/// Per connection. The town has no quest NPC placed, so working the markers out walks no creature (#973 row 1 grows
/// with them).
/// </summary>
[MemoryDiagnoser]
public class QuestFlushBenchmarks
{
    private const uint KillQuest = 1;
    private const int Connections = 300;

    private ScenarioWorld _scenario = null!;
    private QuestService _quests = null!;
    private QuietConnection[] _connections = null!;
    private CharacterEntity[] _characters = null!;
    private uint _objective;

    [GlobalSetup]
    public void Setup()
    {
        _scenario = ScenarioWorld.CreateWithReferenceData();
        MapInstance town = _scenario.AddInstance(MapType.Town, 1);
        _quests = new QuestService(_scenario.Host, new ServiceCollection().BuildServiceProvider(),
            new CharacterEconomy(_scenario.Host, new ItemIdAllocator()), new LootRandom(new Random(0)), _scenario.Clock,
            NullLogger<QuestService>.Instance);
        if (!_quests.Catalog.TryGet(KillQuest, out QuestView? quest))
            throw new InvalidOperationException("The seed has no quest 1.");
        _objective = quest.Stages[0].Objectives[0].Id;

        _connections = new QuietConnection[Connections];
        _characters = new CharacterEntity[Connections];
        for (int i = 0; i < Connections; i++)
        {
            CharacterEntity character = _scenario.NewCharacter(CharacterClass.Warrior);
            character.InstanceId = town.InstanceId;
            character.Quests.Start(KillQuest, DateTime.UnixEpoch);
            // The log went out at select: what follows is the steady flush, which reads no locale.
            character.Quests.LogSent = true;
            _characters[i] = character;
            _connections[i] = new QuietConnection(character);
            QuestFlusher.Flush(_connections[i], _quests);
        }

        if (Sent(NoChange) != 0 || Sent(OneChanged) != 1 || Sent(AllChanged) != Connections)
            throw new InvalidOperationException("The flush did not send what the changes owed.");
    }

    [Benchmark(OperationsPerInvoke = Connections)]
    public void NoChange() => FlushAll();

    [Benchmark(OperationsPerInvoke = Connections)]
    public void OneChanged()
    {
        Credit(_characters[0]);
        FlushAll();
    }

    [Benchmark(OperationsPerInvoke = Connections)]
    public void AllChanged()
    {
        for (int i = 0; i < _characters.Length; i++)
            Credit(_characters[i]);
        FlushAll();
    }

    /// <summary>Moves the kill count between 1 and 2, below the objective's count, so the stage never completes.</summary>
    private void Credit(CharacterEntity character)
    {
        QuestLog log = character.Quests;
        ActiveQuest active = log.Get(KillQuest)!;
        log.SetProgress(active, _objective, active.ProgressOf(_objective) == 1 ? 2u : 1u);
    }

    private void FlushAll()
    {
        for (int i = 0; i < _connections.Length; i++)
            QuestFlusher.Flush(_connections[i], _quests);
    }

    /// <summary>The packets one run of <paramref name="run" /> sent, over every connection: setup's check only.</summary>
    private int Sent(Action run)
    {
        int before = 0, after = 0;
        foreach (QuietConnection connection in _connections)
            before += connection.Sent;
        run();
        foreach (QuietConnection connection in _connections)
            after += connection.Sent;
        return after - before;
    }

    [GlobalCleanup]
    public void Cleanup() => _scenario.Dispose();
}
