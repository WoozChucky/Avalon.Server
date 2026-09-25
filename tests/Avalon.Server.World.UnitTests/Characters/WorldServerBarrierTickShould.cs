using System.Net;
using System.Net.Sockets;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Entities;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// The barrier only bounds anything because the tick sweeps it. A policy nothing calls releases
/// nobody, and every login is on that path until a client sends CMSG_CHARACTER_LOADED. The same
/// holds for the per-tick inventory update: the flusher only reaches a client because the tick
/// calls it.
/// </summary>
public class WorldServerBarrierTickShould : IDisposable
{
    private readonly TcpClient _clientSide;
    private readonly TcpClient _serverSide;

    public WorldServerBarrierTickShould()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _clientSide = new TcpClient();
        _clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        _serverSide = listener.AcceptTcpClient();
        listener.Stop();
    }

    public void Dispose()
    {
        _clientSide.Dispose();
        _serverSide.Dispose();
    }

    [Fact]
    public void Spawn_a_character_whose_client_never_reported_in()
    {
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build();
        IMapInstance instance = Substitute.For<IMapInstance>();
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), instance,
            DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(16).Ticks);

        server.Tick();

        world.Received(1).SpawnInInstance(connection, instance);
    }

    [Fact]
    public void Leave_a_character_alone_while_its_client_still_has_time()
    {
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build();
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), Substitute.For<IMapInstance>(),
            DateTime.UtcNow.Ticks);

        server.Tick();

        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
    }

    [Fact]
    public void Send_a_characters_inventory_changes_on_the_tick_they_were_made()
    {
        (TestWorldServer server, _, Avalon.World.WorldConnection connection) = Build();
        connection.CryptoSession.Initialize(new CryptoManager().GetPublicKey());

        CharacterEntity character = New();
        connection.Character = character;
        InventoryFor(character).TryAdd(Potion.Id, 1);

        server.Tick();

        Assert.False(character.ClientChanges.HasChanges);
    }

    private (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) Build()
    {
        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration { CharacterLoadTimeoutSeconds = 15 });

        var server = new TestWorldServer(world);
        var connection = new Avalon.World.WorldConnection(
            server, _clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        server.Add(connection);
        return (server, world, connection);
    }

    /// <summary>Reaches one tick without the socket loop that normally drives it.</summary>
    private sealed class TestWorldServer : WorldServer
    {
        public TestWorldServer(IWorld world) : base(
            Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance,
            new AnyServiceProvider(),
            Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = 0 }),
            world,
            Substitute.For<IScriptManager>(),
            Substitute.For<IReplicatedCache>(),
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<Avalon.World.Persistence.ICharacterSaver>())
        { }

        public void Add(Avalon.World.WorldConnection connection) => AddConnection(connection);

        public void Tick() => Update(TimeSpan.FromMilliseconds(16), 0);
    }

    /// <summary>
    /// The world server reflects over every packet handler in the assembly and activates each one,
    /// so standing it up needs a container that answers for all of their dependencies.
    /// </summary>
    private sealed class AnyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ILoggerFactory)) return NullLoggerFactory.Instance;

            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
                return Activator.CreateInstance(
                    typeof(NullLogger<>).MakeGenericType(serviceType.GenericTypeArguments[0]));

            if (serviceType.IsInterface || serviceType.IsAbstract)
                return Substitute.For([serviceType], []);

            return serviceType.GetConstructor(Type.EmptyTypes) is not null
                ? Activator.CreateInstance(serviceType)
                : null;
        }
    }
}
