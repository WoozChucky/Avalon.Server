// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// A character is written back to the database by its despawn, and the despawn is queued by the
/// close and run by the tick. Shutdown stops the tick before closing anything, so the pass that
/// runs them afterwards is the only thing standing between a clean restart and every logged-in
/// character left online at a stale position.
/// </summary>
public class WorldServerShutdownShould : IDisposable
{
    private readonly TcpClient _clientSide;
    private readonly TcpClient _serverSide;

    public WorldServerShutdownShould()
    {
        (_clientSide, _serverSide) = CreateLoopbackPair();
    }

    public void Dispose()
    {
        _clientSide.Dispose();
        _serverSide.Dispose();
    }

    [Fact]
    public async Task DespawnAConnectionItClosed()
    {
        Avalon.World.World world = SubstituteWorld();
        var server = new TestWorldServer(world);

        var connection = new Avalon.World.WorldConnection(
            server, _clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        server.Add(connection);

        await server.Stop();

        await world.Received(1).DeSpawnPlayerAsync(connection);
    }

    private static Avalon.World.World SubstituteWorld() =>
        Substitute.For<Avalon.World.World>(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration()),
            new AnyServiceProvider(),
            Substitute.For<IWorldRepository>(),
            Substitute.For<IAvalonMapManager>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICharacterCreateInfoRepository>(),
            Substitute.For<IClassLevelStatRepository>(),
            Substitute.For<IItemTemplateRepository>(),
            Substitute.For<IAbilityTemplateRepository>(),
            Substitute.For<ICharacterLevelExperienceRepository>(),
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<IChunkLibrary>());

    private static (TcpClient clientSide, TcpClient serverSide) CreateLoopbackPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, port);
        var serverSide = listener.AcceptTcpClient();
        listener.Stop();
        return (clientSide, serverSide);
    }

    /// <summary>
    /// The world server reflects over every packet handler in the assembly and activates each one,
    /// so standing it up needs a container that answers for all of their dependencies rather than a
    /// list of the ones this test cares about.
    /// </summary>
    private sealed class AnyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ILoggerFactory)) return NullLoggerFactory.Instance;

            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
                return Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(serviceType.GenericTypeArguments[0]));

            if (serviceType.IsInterface || serviceType.IsAbstract)
                return Substitute.For([serviceType], []);

            // Handlers also take plain settings objects; a default one is enough to construct them.
            return serviceType.GetConstructor(Type.EmptyTypes) is not null
                ? Activator.CreateInstance(serviceType)
                : null;
        }
    }

    /// <summary>Reaches the two members the shutdown path needs: the connection set, and the stop itself.</summary>
    private sealed class TestWorldServer : WorldServer
    {
        public TestWorldServer(Avalon.World.World world) : base(
            Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance,
            new AnyServiceProvider(),
            Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = 0 }),
            world,
            Substitute.For<IScriptManager>(),
            Substitute.For<ICreatureSpawner>(),
            Substitute.For<IReplicatedCache>(),
            Substitute.For<IScriptHotReloader>())
        { }

        public void Add(Avalon.World.WorldConnection connection) => AddConnection(connection);

        public Task Stop() => OnStoppingAsync(CancellationToken.None);
    }
}
