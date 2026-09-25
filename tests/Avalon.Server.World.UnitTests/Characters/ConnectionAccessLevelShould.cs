using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Avalon.Common.Accounts;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Characters;

public class ConnectionAccessLevelShould
{
    [Fact]
    public void Expose_No_Setter_On_The_Public_Interface()
    {
        // The modding-API rule as a test. Someone adding "set;" to make a test easier fails here,
        // and this comment says why: a settable level would let any mod make any player a GM.
        PropertyInfo property = typeof(IWorldConnection).GetProperty(nameof(IWorldConnection.AccessLevel))!;

        Assert.Null(property.SetMethod);
    }

    [Fact]
    public void Default_A_Connections_Access_Level_To_Player()
    {
        // So a GM whose account read has not landed (or never lands) is denied, never the reverse.
        // Driven against the real connection, not a substitute, for the same reason
        // CharacterLocaleAndGenderShould.Default_A_Connections_Locale_To_enUS is: a substituted
        // interface property just echoes back whatever value was configured, which would never
        // exercise the production `= AccountAccessLevel.Player` initializer at all.
        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).SendBufferCapacity.Returns(256);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();

        var connection = new Avalon.World.WorldConnection(
            server, clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        try
        {
            Assert.Equal(AccountAccessLevel.Player, connection.AccessLevel);
        }
        finally
        {
            connection.Close();
            connection.Dispose();
            serverSide.Dispose();
        }
    }
}
