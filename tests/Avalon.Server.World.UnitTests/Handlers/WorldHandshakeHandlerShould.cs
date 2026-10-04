using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Auth;
using Avalon.Server.World.Handlers;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

public sealed class WorldHandshakeHandlerShould
{
    private static WorldHandshakeHandler Handler(string minimum = "0.0.1")
    {
        var world = Substitute.For<IWorld>(); world.MinVersion.Returns(minimum);
        return new(NullLogger<WorldHandshakeHandler>.Instance, world);
    }
    [Fact]
    public async Task Accept_supported_client_only_after_admission_and_request_the_initial_ping()
    {
        using var connection = WorldAdmissionConnection.Create();
        connection.CryptoSession.Initialize(new CryptoManager().GetPublicKey());
        connection.PublishAdmission(WorldAdmissionConnection.Lease());
        await Handler().ExecuteAsync(new() { Connection = connection, Packet = new() { Version = "0.2.0" } });
        Assert.True(connection.IsGameplayAuthorized);
        Assert.Single(connection.Sent);
        Assert.True(connection.TakeInitialTimeSyncPingRequest());
        Assert.False(connection.TakeInitialTimeSyncPingRequest());
    }
    [Theory]
    [InlineData("0.1.0")]
    [InlineData("invalid")]
    [InlineData("")]
    public async Task Reject_old_or_malformed_clients_even_when_their_account_was_admitted(string version)
    {
        using var connection = WorldAdmissionConnection.Create();
        connection.PublishAdmission(WorldAdmissionConnection.Lease());
        await Handler().ExecuteAsync(new() { Connection = connection, Packet = new() { Version = version } });
        Assert.True(connection.IsClosing); Assert.False(connection.IsGameplayAuthorized);
        Assert.False(connection.TakeInitialTimeSyncPingRequest());
    }
    [Fact]
    public async Task Reject_account_id_without_admitted_authority()
    {
        using var connection = WorldAdmissionConnection.Create(); connection.AccountId = 42;
        await Handler().ExecuteAsync(new() { Connection = connection, Packet = new() { Version = "0.2.0" } });
        Assert.True(connection.IsClosing); Assert.False(connection.IsGameplayAuthorized);
    }
    [Fact]
    public async Task Preserve_a_worlds_higher_supported_version_boundary()
    {
        using var connection = WorldAdmissionConnection.Create(); connection.PublishAdmission(WorldAdmissionConnection.Lease());
        await Handler("0.3.0").ExecuteAsync(new() { Connection = connection, Packet = new() { Version = "0.2.0" } });
        Assert.True(connection.IsClosing); Assert.False(connection.IsGameplayAuthorized);
    }
}
