using System.Text;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// #504: a logged-in game-client connection kept the access it logged in with after an admin
/// changed the account's roles. The role write raises the credentials version, so the connection's
/// next post-login step closes it even if the disconnect message never arrives, and the message
/// itself closes it at once (<see cref="AuthServer.CloseAccountConnections"/>).
/// </summary>
public sealed class RoleChangeSessionShould : IDisposable
{
    private readonly AuthSqlite _database = new();

    public void Dispose() => _database.Dispose();

    private async Task<Account> AccountAsync() => await new AccountRepository(_database).CreateAsync(new Account
    {
        Username = "STAFFER",
        Email = "staffer@example.com",
        Salt = [1],
        Verifier = Encoding.UTF8.GetBytes("unused"),
        JoinDate = DateTime.UtcNow,
        LastLogin = DateTime.UtcNow,
        AccessLevel = AccountAccessLevel.Player | AccountAccessLevel.GameMaster,
    });

    private static IAuthConnection LoggedIn(Account account)
    {
        IAuthConnection connection = Substitute.For<IAuthConnection>();
        connection.AccountId.Returns(account.Id);
        connection.CredentialsVersion.Returns(account.CredentialsVersion);
        connection.RemoteEndPoint.Returns("127.0.0.1:1");
        return connection;
    }

    private async Task ChangeRolesAsync(AccountId id, AccountAccessLevel level)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal(1, await AccountRepository.SetAccessLevelAsync(context, id, level));
    }

    [Fact]
    public async Task Close_a_connection_that_logged_in_before_its_roles_changed()
    {
        Account account = await AccountAsync();
        IAuthConnection connection = LoggedIn(account);

        await ChangeRolesAsync(account.Id, AccountAccessLevel.Player);

        Account? admitted = await PostLoginGuard.AccountOrCloseAsync(connection, new AccountRepository(_database),
            NullLogger.Instance, "world select", default);
        Assert.Null(admitted);
        connection.Received(1).Close();
    }

    [Fact]
    public async Task Close_the_connection_when_the_role_changes_disconnect_arrives()
    {
        Account account = await AccountAsync();
        IAuthConnection connection = LoggedIn(account);

        await ChangeRolesAsync(account.Id, AccountAccessLevel.Player);
        // The message the API publishes after the change: the bare account id.
        int closed = AuthServer.CloseAccountConnections([connection], account.Id.Value.ToString(), NullLogger.Instance);

        Assert.Equal(1, closed);
        connection.Received(1).Close();
    }
}
