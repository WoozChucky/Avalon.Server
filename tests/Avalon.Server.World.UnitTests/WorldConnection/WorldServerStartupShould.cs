using System.Net;
using System.Net.Sockets;
using Avalon.Configuration;
using Avalon.Database.Character.Repositories;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Parties;
using Avalon.World.Quests;
using Avalon.World.Persistence;
using Avalon.World.Pvp;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// The world server's port opens only once the server can serve what arrives on it (#665): scripts
/// and the world loaded, the cache subscribed, the connection listener registered and the tick
/// running. Before, <c>ServerBase.StartAsync</c> listened at host start, and clients were accepted
/// while <c>World.LoadAsync</c> was still running, or after it had failed.
/// </summary>
public class WorldServerStartupShould
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private readonly int _port = FreePort();
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly TaskCompletionSource _load = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WorldServerStartupShould()
    {
        _world.LoadAsync(Arg.Any<CancellationToken>()).Returns(_load.Task);
        _world.Configuration.Returns(new GameConfiguration());
        // The server subscribes to its own world's reload channel, so it needs to know its id.
        _world.Id.Returns(new Avalon.Domain.Auth.WorldId(1));
    }

    [Fact]
    public async Task Refuse_connections_until_the_world_has_loaded()
    {
        var server = new TestWorldServer(_world, _port);
        await server.StartAsync(CancellationToken.None);
        try
        {
            Assert.False(await AcceptsAsync(), "a client was accepted while the world was still loading");

            _load.SetResult();

            await UntilAsync(AcceptsAsync, "the port did not open once the world had loaded");
        }
        finally
        {
            await server.StopAsync(CancellationToken.None).WaitAsync(Limit);
        }
    }

    [Fact]
    public async Task Never_open_the_port_when_the_world_fails_to_load()
    {
        var server = new TestWorldServer(_world, _port);
        await server.StartAsync(CancellationToken.None);
        try
        {
            _load.SetException(new InvalidOperationException("simulated load failure"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => server.ExecuteTask!.WaitAsync(Limit));
            Assert.False(await AcceptsAsync(), "a client was accepted after the world failed to load");
        }
        finally
        {
            await server.StopAsync(CancellationToken.None).WaitAsync(Limit);
        }
    }

    /// <summary>A stop that lands during the load leaves the port shut when the load then finishes.</summary>
    [Fact]
    public async Task Keep_the_port_shut_when_stopped_during_the_load()
    {
        var server = new TestWorldServer(_world, _port);
        await server.StartAsync(CancellationToken.None);

        Task stopping = server.StopAsync(CancellationToken.None);
        _load.SetResult();
        await stopping.WaitAsync(Limit);

        Assert.False(await AcceptsAsync(), "the port opened after the server had been stopped");
    }

    /// <summary>Whether a client can connect to the server's port right now.</summary>
    private async Task<bool> AcceptsAsync()
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, _port).WaitAsync(Limit);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task UntilAsync(Func<Task<bool>> done, string failure)
    {
        DateTime deadline = DateTime.UtcNow + Limit;
        while (!await done())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(10);
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private sealed class TestWorldServer(IWorld world, int port) : WorldServer(
        Substitute.For<IPacketManager>(),
        NullLoggerFactory.Instance,
        new AnyServiceProvider(),
        Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = (ushort)port }),
        world,
        Substitute.For<IScriptManager>(),
        Substitute.For<IReplicatedCache>(),
        Substitute.For<IScriptHotReloader>(),
        new CharacterSaver(Substitute.For<ICharacterSaveRepository>(), NullLogger<CharacterSaver>.Instance),
        new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System, NullLogger<PartyService>.Instance));

    /// <summary>
    /// The world server reflects over every packet handler in the assembly and activates each one,
    /// so standing it up needs a container that answers for all of their dependencies.
    /// </summary>
    private sealed class AnyServiceProvider : IServiceProvider
    {
        private QuestService? _quests;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ILoggerFactory)) return NullLoggerFactory.Instance;

            if (serviceType == typeof(PvpToggle))
                return new PvpToggle(Options.Create(new GameConfiguration()), TimeProvider.System);

            // The party handlers (2026-09-30) take the one party service, a class with settings, a clock and a logger.
            if (serviceType == typeof(PartyService))
                return new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System,
                    NullLogger<PartyService>.Instance);

            // The quest handlers (#433) and the world server take the one quest service, as production's singleton.
            if (serviceType == typeof(QuestService))
                return _quests ??= Avalon.Server.World.UnitTests.Quests.InertQuestService.Create();

            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
                return Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(serviceType.GenericTypeArguments[0]));

            if (serviceType.IsInterface || serviceType.IsAbstract)
                return Substitute.For([serviceType], []);

            return serviceType.GetConstructor(Type.EmptyTypes) is not null
                ? Activator.CreateInstance(serviceType)
                : null;
        }
    }
}
