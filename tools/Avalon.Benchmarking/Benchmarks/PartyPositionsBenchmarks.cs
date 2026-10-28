using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Hosting.Networking;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Parties;
using Avalon.World.Public.Enums;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The far party members' positions flush (#1199) for a party of six spread across one instance, 100 m apart on a line,
/// so every member is out of every other's view: each due flush sends each member its five far members, one
/// <c>SMSG_PARTY_POSITIONS</c> each, framed and sealed by the send pass as a tick ends. Each call moves the clock on one
/// <see cref="PartyService.PositionsInterval" />, so every member is due, the most a flush of this party does.
/// <see cref="Idle" /> is the same party on the 59 ticks of 60 between: the clock stays, nobody is due, nothing is sent.
/// </summary>
[MemoryDiagnoser]
public class PartyPositionsBenchmarks
{
    private const int Members = 6;

    private ScenarioWorld _world = null!;
    private PartyService _parties = null!;
    private ScenarioConnection[] _connections = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = ScenarioWorld.CreateWithReferenceData();
        MapInstance town = _world.AddInstance(MapType.Town);
        _parties = _world.Parties!;

        _connections = new ScenarioConnection[Members];
        for (int i = 0; i < Members; i++)
        {
            CharacterEntity character = _world.NewCharacter(CharacterClass.Warrior);
            character.Name = SocialSchedule.NameOf(i);
            _connections[i] = _world.Connect(character);
        }

        _world.FormParty(_connections);
        for (int i = 0; i < Members; i++)
            _world.Join(town, _connections[i], new Vector3(i * 100f, 0f, 0f));

        // The first tick decides every view (each member sees itself only) and sends the first lists.
        _world.Tick();

        int before = Sent();
        SpreadPartyOfSix();
        if (Sent() - before != Members)
            throw new InvalidOperationException($"A due flush sent {Sent() - before} lists, not {Members}.");
    }

    [Benchmark]
    public void SpreadPartyOfSix()
    {
        NetworkSendScheduler.DeferSignalsOnCurrentThread();
        try
        {
            _world.Clock.Advance(PartyService.PositionsInterval);
            _parties.FlushPositions();
            _world.Scheduler.SignalAll();
            _world.Scheduler.RunAllPasses();
        }
        finally
        {
            NetworkSendScheduler.ResumeSignalsOnCurrentThread();
        }
    }

    /// <summary>A flush of the same watched party with nobody due, as on most ticks.</summary>
    [Benchmark]
    public void Idle() => _parties.FlushPositions();

    private int Sent()
    {
        int sent = 0;
        foreach (ScenarioConnection connection in _connections)
            sent += connection.Sent;
        return sent;
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();
}
