using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Avalon.Network.Packets;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// #495: a password change, an MFA reset, an MFA removal or a ban publishes the account on
/// <c>world:accounts:disconnect</c>, and only the World server listened, so a logged-in
/// auth-server connection kept asking for world keys with the old credentials. The auth server now
/// listens too and closes every connection logged in as that account.
/// </summary>
public sealed class AccountDisconnectShould
{
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();

    private AuthServer Server()
    {
        var hosting = Substitute.For<IOptions<HostingConfiguration>>();
        hosting.Value.Returns(new HostingConfiguration { Port = 0, Host = "127.0.0.1" });
        var security = Substitute.For<IOptions<HostingSecurity>>();
        security.Value.Returns(new HostingSecurity());
        return new AuthServer(Substitute.For<IServiceProvider>(), Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance, Substitute.For<IAccountRepository>(), _cache, hosting, security);
    }

    private static IAuthConnection Connection(long? accountId)
    {
        IAuthConnection connection = Substitute.For<IAuthConnection>();
        connection.AccountId.Returns(accountId is { } id ? new AccountId(id) : null);
        connection.RemoteEndPoint.Returns("127.0.0.1:1");
        return connection;
    }

    [Fact]
    public async Task Listen_on_the_account_disconnect_channel()
    {
        await Server().SubscribeToAccountDisconnectsAsync();

        await _cache.Received(1).SubscribeAsync(CacheKeys.WorldAccountsDisconnectChannel,
            Arg.Any<Action<RedisChannel, RedisValue>>());
    }

    [Fact]
    public async Task Survive_a_message_for_an_account_it_holds_no_connection_of()
    {
        Action<RedisChannel, RedisValue>? handler = null;
        await _cache.SubscribeAsync(CacheKeys.WorldAccountsDisconnectChannel,
            Arg.Do<Action<RedisChannel, RedisValue>>(h => handler = h));

        await Server().SubscribeToAccountDisconnectsAsync();

        Assert.NotNull(handler);
        handler!(RedisChannel.Literal(CacheKeys.WorldAccountsDisconnectChannel), "7");
    }

    [Fact]
    public void Close_every_connection_logged_in_as_the_account_and_tell_it_why()
    {
        IAuthConnection first = Connection(7);
        IAuthConnection second = Connection(7);

        int closed = AuthServer.CloseAccountConnections([first, second], "7", NullLogger.Instance);

        Assert.Equal(2, closed);
        foreach (IAuthConnection connection in new[] { first, second })
        {
            connection.Received(1).Send(Arg.Any<NetworkPacket>());
            connection.Received(1).Close();
        }
    }

    [Fact]
    public void Leave_other_accounts_and_connections_not_logged_in_alone()
    {
        IAuthConnection other = Connection(8);
        IAuthConnection anonymous = Connection(null);
        IAuthConnection target = Connection(7);

        int closed = AuthServer.CloseAccountConnections([other, anonymous, target], "7", NullLogger.Instance);

        Assert.Equal(1, closed);
        target.Received(1).Close();
        other.DidNotReceiveWithAnyArgs().Close();
        anonymous.DidNotReceiveWithAnyArgs().Close();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("-7")]
    public void Ignore_a_message_that_names_no_account(string message)
    {
        IAuthConnection connection = Connection(7);

        int closed = AuthServer.CloseAccountConnections([connection], message, NullLogger.Instance);

        Assert.Equal(0, closed);
        connection.DidNotReceiveWithAnyArgs().Close();
    }
}
