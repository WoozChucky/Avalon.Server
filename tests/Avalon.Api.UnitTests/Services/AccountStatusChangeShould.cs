using Avalon.Api.Config;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// A ban against a real database, through the real transaction runner. All three writes run on the
/// one context the runner opens; a ban that revoked no credentials would leave the banned account
/// a live session for as long as its tokens last.
/// </summary>
public class AccountStatusChangeShould : IDisposable
{
    private readonly SqliteAuthDatabase _database = new();

    [Fact]
    public async Task Revoke_every_credential_the_account_holds()
    {
        AccountId actor = new(1);
        AccountRepository accounts = new(_database);

        Account account = await accounts.CreateAsync(new Account
        {
            Username = "BANME",
            Email = "banme@avalon.monster",
            Salt = [1],
            Verifier = [2],
            SessionKey = [],
            LastIp = "127.0.0.1",
            LastAttemptIp = string.Empty,
            MuteBy = string.Empty,
            MuteReason = string.Empty,
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        });

        await new RefreshTokenRepository(_database).CreateAsync(new RefreshToken
        {
            AccountId = account.Id,
            FamilyId = Guid.NewGuid(),
            Index = 0,
            Hash = [9, 9, 9],
            Revoked = false,
            Usages = 0,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });

        await new PersonalAccessTokenRepository(_database).CreateAsync(new PersonalAccessToken
        {
            AccountId = account.Id,
            Name = "cli",
            TokenHash = [7, 7, 7],
            TokenPrefix = "avp_1234",
            Roles = AccountAccessLevel.Player,
            CreatedAt = DateTime.UtcNow,
        });

        AccountService service = new(
            NullLoggerFactory.Instance,
            accounts,
            Substitute.For<Avalon.Api.Authentication.Jwt.IJwtUtils>(),
            Substitute.For<IMFAHashService>(),
            new MfaSetupRepository(_database),
            new DeviceRepository(_database),
            Substitute.For<IReplicatedCache>(),
            Substitute.For<ISecureRandom>(),
            Substitute.For<IRefreshTokenService>(),
            new DbTransactionRunner<AuthDbContext>(_database),
            new AuthenticationConfig());

        await service.UpdateStatusAsync(account.Id, Contract.AccountStatus.Banned, "spam", actor);

        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal(AccountStatus.Banned, (await context.Accounts.SingleAsync(a => a.Id == account.Id)).Status);
        Assert.True(await context.RefreshTokens.Where(t => t.AccountId == account.Id).AllAsync(t => t.Revoked));
        Assert.True(await context.PersonalAccessTokens.Where(t => t.AccountId == account.Id)
            .AllAsync(t => t.RevokedAt != null && t.RevokedBy == actor));
    }

    public void Dispose() => _database.Dispose();
}
