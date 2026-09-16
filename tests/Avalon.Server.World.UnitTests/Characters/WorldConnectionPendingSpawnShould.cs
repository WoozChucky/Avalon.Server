using System.Net;
using System.Net.Sockets;
using Avalon.Hosting.Networking;
using Avalon.World;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// The real connection's half of the barrier, held to the two properties everything above it
/// assumes: a selected character is not yet the connection's Character, and a pending spawn can
/// only be taken once.
/// </summary>
public class WorldConnectionPendingSpawnShould : IDisposable
{
    private readonly Avalon.World.WorldConnection _connection;
    private readonly TcpClient _serverSide;

    public WorldConnectionPendingSpawnShould()
    {
        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).SendBufferCapacity.Returns(256);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        _serverSide = listener.AcceptTcpClient();
        listener.Stop();

        _connection = new Avalon.World.WorldConnection(
            server, clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _serverSide.Dispose();
    }

    [Fact]
    public void Leave_the_character_unset_while_a_spawn_is_pending()
    {
        _connection.SetPendingSpawn(Substitute.For<ICharacter>(), Substitute.For<IMapInstance>(), 1234L);

        Assert.NotNull(_connection.PendingSpawn);
        Assert.Null(_connection.Character);
        Assert.False(_connection.InGame);
    }

    [Fact]
    public void Hand_out_a_pending_spawn_once()
    {
        ICharacter character = Substitute.For<ICharacter>();
        IMapInstance instance = Substitute.For<IMapInstance>();
        _connection.SetPendingSpawn(character, instance, 1234L);

        PendingSpawn? first = _connection.TakePendingSpawn();

        Assert.NotNull(first);
        Assert.Same(character, first.Character);
        Assert.Same(instance, first.Instance);
        Assert.Equal(1234L, first.SinceTicks);
        Assert.Null(_connection.TakePendingSpawn());
        Assert.Null(_connection.PendingSpawn);
    }

    [Fact]
    public void Have_nothing_pending_before_a_character_is_selected()
    {
        Assert.Null(_connection.PendingSpawn);
        Assert.Null(_connection.TakePendingSpawn());
    }
}
