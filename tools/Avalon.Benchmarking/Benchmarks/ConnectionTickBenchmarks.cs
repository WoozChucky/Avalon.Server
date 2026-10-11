using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.GameAuth;
using Avalon.World.Public;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>The production readiness, lease and input-filter paths, per connection at 60 Hz (#880).</summary>
[MemoryDiagnoser]
public class ConnectionTickBenchmarks
{
    private const int Connections = 300;
    private readonly FixedClock _clock = new();
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
    }

    [Benchmark(OperationsPerInvoke = Connections)]
    public void ReadinessSweep() => CharacterReadinessBarrier.ReleaseExpired(_connections, _world,
        _clock.GetUtcNow().Ticks, TimeSpan.FromSeconds(15), NullLogger.Instance);

    [Benchmark(OperationsPerInvoke = Connections)]
    public void LeaseChecks()
    {
        for (int i = 0; i < Connections; i++)
            _connection.AdvanceGameplayLease();
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

    [GlobalCleanup]
    public void Cleanup()
    {
        _connection.Dispose();
        _peer.Dispose();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
        public override long GetTimestamp() => 0;
    }
}
