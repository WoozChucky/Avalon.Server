using Avalon.Common;
using Avalon.Domain.World;
using Avalon.World.Configuration;
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
using Microsoft.Extensions.Options;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The tick's quest flush (#977): <c>QuestFlusher.Flush</c> over <see cref="Connections" /> connections, as WorldServer
/// runs it for every connection every tick, through the real <see cref="QuestService" /> over the World server's
/// reference data (the seeded quests) and a real town instance. Each character holds the seeded quest 1 (a kill
/// objective), has had its log sent and has flushed once, so what is left is the steady flush. <see cref="NoChange" />:
/// nothing changed since the last flush, the common tick. <see cref="OneChanged" />: one character's kill count moved,
/// so it is owed an update and its markers are worked out again. <see cref="AllChanged" />: every character's moved.
/// Per connection. The town has no quest NPC placed, so working the markers out walks no creature (#973 row 1 grows
/// with them). <see cref="ResetPassed" /> (#710): the tick a daily reset passes, the one tick on which every connection
/// skips the #977 early exit. Its own setup places the town's seven seeded NPCs (Hilde, who gives the daily quest 10,
/// among them) and gives every character, at level 10, a completion of quest 10, so each one's reset step finds a
/// rolled completion, its markers are worked out again over the seven creatures and the changed list is sent. The
/// reset clock goes one daily boundary forward, then back before it, on alternate invocations, so every invocation is
/// such a tick (the blue marker shows, then goes): its one <c>QuestResetClock.Advance</c> is part of that tick too.
/// </summary>
[MemoryDiagnoser]
public class QuestFlushBenchmarks
{
    private const uint KillQuest = 1;
    private const uint DailyQuest = 10;
    private const int Connections = 300;

    // The town's NPCs, as its MapCreatureSpawns rows place them on map 1.
    private static readonly ulong[] s_townNpcs = [1, 2, 3, 11, 12, 13, 14];

    private ScenarioWorld _scenario = null!;
    private QuestService _quests = null!;
    private QuietConnection[] _connections = null!;
    private CharacterEntity[] _characters = null!;
    private uint _objective;
    private DateTime _afterReset;
    private DateTime _beforeReset;
    private bool _forward;

    [GlobalSetup]
    public void Setup()
    {
        _scenario = ScenarioWorld.CreateWithReferenceData();
        MapInstance town = _scenario.AddInstance(MapType.Town, 1);
        var ids = new ItemIdAllocator();
        var roller = new ItemRoller(new LootRandom(new Random(0)), ids, Options.Create(new GameConfiguration()));
        _quests = new QuestService(_scenario.Host, new ServiceCollection().BuildServiceProvider(),
            new CharacterEconomy(_scenario.Host, ids, roller), new LootRandom(new Random(0)), roller, _scenario.Clock,
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

    [GlobalSetup(Target = nameof(ResetPassed))]
    public void SetupResetPassed()
    {
        Setup();
        MapInstance town = _scenario.Maps[0];
        uint guid = 1;
        foreach (CreatureTemplate metadata in _scenario.Host.Data.CreatureTemplates.Where(t => s_townNpcs.Contains(t.Id.Value)))
        {
            town.AddCreature(new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, guid++),
                TemplateId = metadata.Id,
                Metadata = metadata,
                Name = metadata.Name,
                Position = TownNavmesh.EntrySpawn,
                Health = 100,
                CurrentHealth = 100,
            });
        }

        if (guid != s_townNpcs.Length + 1)
            throw new InvalidOperationException("The seed lacks a town NPC.");

        // Turned in 8 h after the latest daily boundary: done for this period, available again after the next boundary.
        QuestResetClock resets = _quests.Resets;
        DateTime boundary = resets.LastBoundary(QuestRepeat.Daily);
        _beforeReset = boundary.AddHours(9);
        _afterReset = boundary.AddDays(1).AddHours(1);
        foreach (CharacterEntity character in _characters)
        {
            character.Level = 10;
            character.Quests.Complete(DailyQuest, boundary.AddHours(8));
        }

        resets.Advance(_beforeReset);
        FlushAll();
        _forward = true;
        int generation = resets.Generation;
        if (Sent(ResetPassed) != Connections || Sent(ResetPassed) != Connections || resets.Generation != generation + 2)
            throw new InvalidOperationException("A reset did not move every character's markers.");
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

    [Benchmark(OperationsPerInvoke = Connections)]
    public void ResetPassed()
    {
        _quests.Resets.Advance(_forward ? _afterReset : _beforeReset);
        _forward = !_forward;
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
