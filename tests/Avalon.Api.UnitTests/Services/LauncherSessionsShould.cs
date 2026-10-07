using System.Text;
using Avalon.Api.Testing;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// The account's launcher sessions (#591): one per live refresh-token family of the launcher kind, on
/// the real schema. A session lives while its newest token does.
/// </summary>
public sealed class LauncherSessionsShould : IDisposable
{
    private static readonly DateTime s_t0 = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private readonly SqliteAuthDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task<AccountId> AccountAsync(string name)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        var account = new Account
        {
            Username = name,
            Email = $"{name.ToLowerInvariant()}@avalon.monster",
            Salt = [1],
            Verifier = Encoding.UTF8.GetBytes("unused"),
            JoinDate = s_t0,
            LastLogin = s_t0,
        };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account.Id;
    }

    /// <summary>A family of tokens created at the given times; all but the newest revoked (rotated).</summary>
    private async Task<Guid> FamilyAsync(AccountId account, SessionClient client, string? device, DateTime expires,
        bool revokeNewest, params DateTime[] created)
    {
        var family = Guid.CreateVersion7();
        await using AuthDbContext context = _database.CreateDbContext();
        for (int i = 0; i < created.Length; i++)
        {
            context.RefreshTokens.Add(new RefreshToken
            {
                AccountId = account,
                FamilyId = family,
                Index = (uint)i,
                Hash = Guid.NewGuid().ToByteArray(),
                Revoked = i < created.Length - 1 || revokeNewest,
                CreatedAt = created[i],
                ExpiresAt = expires,
                Client = client,
                DeviceName = device,
            });
        }

        await context.SaveChangesAsync();
        return family;
    }

    [Fact]
    public async Task List_only_live_launcher_families_newest_use_first()
    {
        AccountId me = await AccountAsync("ME");
        AccountId other = await AccountAsync("OTHER");
        DateTime later = s_t0.AddDays(30);
        Guid rotated = await FamilyAsync(me, SessionClient.Launcher, "MOTHERSHIP", later, false, s_t0, s_t0.AddHours(1), s_t0.AddHours(2));
        Guid fresh = await FamilyAsync(me, SessionClient.Launcher, "LAPTOP", later, false, s_t0.AddHours(3));
        await FamilyAsync(me, SessionClient.Launcher, "GONE", later, true, s_t0);            // signed out
        await FamilyAsync(me, SessionClient.Launcher, "OLD", s_t0.AddHours(1), false, s_t0);  // expired
        await FamilyAsync(me, SessionClient.Web, null, later, false, s_t0);                 // the website
        await FamilyAsync(other, SessionClient.Launcher, "THEIRS", later, false, s_t0);

        IReadOnlyList<LiveFamily> sessions = await new RefreshTokenRepository(_database)
            .ListLiveFamiliesAsync(me, SessionClient.Launcher, s_t0.AddHours(4));

        Assert.Equal([fresh, rotated], sessions.Select(s => s.FamilyId));
        LiveFamily mothership = sessions[1];
        Assert.Equal("MOTHERSHIP", mothership.DeviceName);
        Assert.Equal(s_t0, mothership.SignedInAt);
        Assert.Equal(s_t0.AddHours(2), mothership.LastUsedAt);
        Assert.Equal(later, mothership.ExpiresAt);
    }

    [Fact]
    public async Task Recognise_only_the_callers_own_launcher_families()
    {
        AccountId me = await AccountAsync("ME");
        Guid family = await FamilyAsync(me, SessionClient.Launcher, "MOTHERSHIP", s_t0.AddDays(30), false, s_t0);
        Guid website = await FamilyAsync(me, SessionClient.Web, null, s_t0.AddDays(30), false, s_t0);
        var repository = new RefreshTokenRepository(_database);

        AccountId other = await AccountAsync("OTHER");

        Assert.True(await repository.IsLauncherFamilyOfAsync(me, family));
        Assert.False(await repository.IsLauncherFamilyOfAsync(other, family));
        Assert.False(await repository.IsLauncherFamilyOfAsync(me, website)); // the website's session is not a launcher's to end
        Assert.False(await repository.IsLauncherFamilyOfAsync(me, Guid.NewGuid()));
    }

    [Fact]
    public async Task Recognise_only_a_live_launcher_family_for_ticket_issuance()
    {
        AccountId me = await AccountAsync("ME");
        AccountId other = await AccountAsync("OTHER");
        DateTime now = s_t0.AddHours(2);
        Guid live = await FamilyAsync(me, SessionClient.Launcher, "MOTHERSHIP", s_t0.AddDays(1), false, s_t0, s_t0.AddHours(1));
        Guid revoked = await FamilyAsync(me, SessionClient.Launcher, "GONE", s_t0.AddDays(1), true, s_t0);
        Guid expired = await FamilyAsync(me, SessionClient.Launcher, "OLD", s_t0.AddHours(1), false, s_t0);
        Guid website = await FamilyAsync(me, SessionClient.Web, null, s_t0.AddDays(1), false, s_t0);
        var repository = new RefreshTokenRepository(_database);

        Assert.True(await repository.IsLiveLauncherFamilyAsync(me, live, now));
        Assert.False(await repository.IsLiveLauncherFamilyAsync(other, live, now));
        Assert.False(await repository.IsLiveLauncherFamilyAsync(me, revoked, now));
        Assert.False(await repository.IsLiveLauncherFamilyAsync(me, expired, now));
        Assert.False(await repository.IsLiveLauncherFamilyAsync(me, website, now));
    }

    [Fact]
    public void Index_tokens_by_family_for_the_lookups_that_start_from_one()
    {
        // FindChildAsync, RevokeFamilyAsync: by family, without the account (#591 review).
        using AuthDbContext context = _database.CreateDbContext();
        IEnumerable<string> indexes = context.Model.FindEntityType(typeof(RefreshToken))!.GetIndexes()
            .Select(i => string.Join(",", i.Properties.Select(p => p.Name)));

        Assert.Contains("FamilyId,Index", indexes);
    }
}
