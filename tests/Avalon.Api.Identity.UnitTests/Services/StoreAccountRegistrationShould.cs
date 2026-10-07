using Avalon.Api.Identity.Config;
using Avalon.Api.Identity.Services;
using Avalon.Api.Testing;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Server.Auth.UnitTests.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Services;

public sealed class StoreAccountRegistrationShould : IDisposable
{
    private readonly SqliteAuthDatabase _db = new();
    private readonly CounterCache _cache = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly StoreAccountRegistration _service;
    public StoreAccountRegistrationShould() => _service = new(new ExternalIdentityRepository(_db, _clock),
        _cache.Cache, new AuthenticationConfig { MaxAccountsCreatedPerSource = 1 }, _clock);
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_a_store_only_root_atomically_without_recovery_credentials()
    {
        IdentityLinkResult result = await _service.CreateFromSteamAsync(Guid.NewGuid(), "76561198000000001", _clock.GetUtcNow().UtcDateTime.AddMinutes(5), "127.0.0.1", CancellationToken.None);
        Assert.Equal(IdentityLinkStatus.Linked, result.Status);
        await using AuthDbContext db = _db.CreateDbContext();
        Account root = await db.Accounts.SingleAsync(x => x.Id == result.Identity!.AccountId);
        Assert.True(root.IsStoreGenerated);
        Assert.Null(root.Email); Assert.Empty(root.Salt); Assert.Empty(root.Verifier);
        Assert.Single(await db.StoreAccountCreations.ToListAsync());
    }

    [Fact]
    public async Task Share_the_creation_budget_and_recover_the_original_operation_without_spending_another_slot()
    {
        var op = Guid.NewGuid();
        DateTime deadline = _clock.GetUtcNow().UtcDateTime.AddMinutes(5);
        Assert.Equal(IdentityLinkStatus.Linked, (await _service.CreateFromSteamAsync(op, "76561198000000001", deadline, "127.0.0.1", CancellationToken.None)).Status);
        Assert.Equal(IdentityLinkStatus.AlreadyLinked, (await _service.CreateFromSteamAsync(op, "76561198000000001", deadline, "127.0.0.1", CancellationToken.None)).Status);
        Assert.Equal(IdentityLinkStatus.CreationRefused, (await _service.CreateFromSteamAsync(Guid.NewGuid(), "76561198000000002", deadline, "127.0.0.1", CancellationToken.None)).Status);
    }
}
