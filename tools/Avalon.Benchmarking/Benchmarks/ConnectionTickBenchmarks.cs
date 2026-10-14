using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.GameAuth;
using Avalon.World.Public;
using Avalon.World.Testing.Fakes;
using Avalon.World.Threading;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The production readiness, lease and input-filter paths, per connection at 60 Hz (#880); and a connection's whole
/// share of a tick outside its handlers (#977): the lease check and both passes, with no packet and with one input.
/// Every clock here reads the system's clock on each call, as production's does, and answers a fixed time: what a
/// read costs is part of what is measured.
/// </summary>
[MemoryDiagnoser]
public class ConnectionTickBenchmarks
{
    private const int Connections = 300;
    private readonly FixedClock _clock = new();
    private readonly CPlayerInputPacket _input = new();
    private ReceiveQueueFixture _fixture = null!;
    private WorldConnection _connection = null!;
    private TcpClient _peer = null!;
    private IWorldConnection[] _connections = null!;
    private IWorld _world = null!;
    private MapSessionFilter _filter = null!;

    [GlobalSetup]
    public void Setup()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        client.Connect((IPEndPoint)listener.LocalEndpoint);
        _peer = listener.AcceptTcpClient();
        _connection = new WorldConnection(Substitute.For<IWorldServer, IServerBase>(), client,
            NullLoggerFactory.Instance, Substitute.For<IPacketReader>(), _clock);
        // Supply transport authentication without a TLS/read loop, outside measurement.
        typeof(WorldConnection).GetField("_tlsAuthenticated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_connection, true);
        GameSessionLease lease = GameSessionLease.TryCreate(new SessionLeaseResponse
        {
            State = "active",
            AccountId = "42",
            GameSessionId = Guid.NewGuid().ToString("D"),
            GameContextId = Guid.NewGuid().ToString("D"),
            FencingToken = "1",
            ServerId = "benchmark",
            WorldId = 1,
            AccessLevel = 1,
            CredentialsVersion = 0,
            SessionEpoch = "0",
            LeaseUntil = _clock.GetUtcNow().UtcDateTime.AddSeconds(44),
            AuthorizationUntil = _clock.GetUtcNow().UtcDateTime.AddMinutes(5),
        }, "benchmark", 1, _clock)!;
        _connection.PublishAdmission(lease);
        _connection.AcceptProtocol();
        _connection.Character = new CharacterEntity { Data = new Character { Map = 2 } };
        _connections = new IWorldConnection[Connections];
        Array.Fill(_connections, _connection);
        _world = Substitute.For<IWorld>();
        _filter = new MapSessionFilter(_connection);

        _fixture = new ReceiveQueueFixture(clockPaysForReads: true);
        if (IdleTicks() != 0 || InputTicks() != Connections)
            throw new InvalidOperationException("The fixture's connection did not dispatch its input.");
    }

    [Benchmark(OperationsPerInvoke = Connections)]
    public void ReadinessSweep() => CharacterReadinessBarrier.ReleaseExpired(_connections, _world,
        _clock.GetUtcNow().Ticks, TimeSpan.FromSeconds(15), NullLogger.Instance);

    [Benchmark(OperationsPerInvoke = Connections)]
    public void LeaseChecks()
    {
        // The tick reads its time once for every connection (#977).
        var now = TickTime.Read(_clock);
        for (int i = 0; i < Connections; i++)
            _connection.AdvanceGameplayLease(now);
    }

    [Benchmark(OperationsPerInvoke = Connections)]
    public int InputFilters()
    {
        int accepted = 0;
        for (int i = 0; i < Connections; i++)
        {
            if (_filter.CanProcess(NetworkPacketType.CMSG_PLAYER_INPUT))
                accepted++;
        }
        return accepted;
    }

    /// <summary>A connection's tick with nothing received: the lease check, then the session and map passes.</summary>
    [Benchmark(OperationsPerInvoke = Connections)]
    public int IdleTicks()
    {
        int before = _fixture.Dispatched;
        var now = TickTime.Read(_fixture.Clock);
        for (int i = 0; i < Connections; i++)
        {
            _fixture.Connection.AdvanceGameplayLease(now);
            _fixture.Pump();
        }

        return _fixture.Dispatched - before;
    }

    /// <summary>The same with one input received (the read loop's queueing of it included), dispatched by the map pass.</summary>
    [Benchmark(OperationsPerInvoke = Connections)]
    public int InputTicks()
    {
        int before = _fixture.Dispatched;
        var now = TickTime.Read(_fixture.Clock);
        for (int i = 0; i < Connections; i++)
        {
            _fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, _input);
            _fixture.Connection.AdvanceGameplayLease(now);
            _fixture.Pump();
        }

        return _fixture.Dispatched - before;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _fixture.Dispose();
        _connection.Dispose();
        _peer.Dispose();
    }

    /// <summary>A fixed time, each read paying for a read of the system's clock (#977).</summary>
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            _ = TimeProvider.System.GetUtcNow();
            return new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
        }

        public override long GetTimestamp()
        {
            _ = TimeProvider.System.GetTimestamp();
            return 0;
        }
    }
}
