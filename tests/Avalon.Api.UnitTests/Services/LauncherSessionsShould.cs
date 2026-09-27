using System.Text;
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
    private static readonly DateTime T0 = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
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
            JoinDate = T0,
            LastLogin = T0,
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
        DateTime later = T0.AddDays(30);
        Guid rotated = await FamilyAsync(me, SessionClient.Launcher, "MOTHERSHIP", later, false, T0, T0.AddHours(1), T0.AddHours(2));
        Guid fresh = await FamilyAsync(me, SessionClient.Launcher, "LAPTOP", later, false, T0.AddHours(3));
        await FamilyAsync(me, SessionClient.Launcher, "GONE", later, true, T0);            // signed out
        await FamilyAsync(me, SessionClient.Launcher, "OLD", T0.AddHours(1), false, T0);  // expired
        await FamilyAsync(me, SessionClient.Web, null, later, false, T0);                 // the website
        await FamilyAsync(other, SessionClient.Launcher, "THEIRS", later, false, T0);

        IReadOnlyList<LiveFamily> sessions = await new RefreshTokenRepository(_database)
            .ListLiveFamiliesAsync(me, SessionClient.Launcher, T0.AddHours(4));

        Assert.Equal([fresh, rotated], sessions.Select(s => s.FamilyId));
        LiveFamily mothership = sessions[1];
        Assert.Equal("MOTHERSHIP", mothership.DeviceName);
        Assert.Equal(T0, mothership.SignedInAt);
        Assert.Equal(T0.AddHours(2), mothership.LastUsedAt);
        Assert.Equal(later, mothership.ExpiresAt);
    }

    [Fact]
    public async Task Name_the_owner_of_a_launcher_family_and_nobody_for_any_other()
    {
        AccountId me = await AccountAsync("ME");
        Guid family = await FamilyAsync(me, SessionClient.Launcher, "MOTHERSHIP", T0.AddDays(30), false, T0);
        Guid website = await FamilyAsync(me, SessionClient.Web, null, T0.AddDays(30), false, T0);
        var repository = new RefreshTokenRepository(_database);

        Assert.Equal(me, await repository.FindLauncherFamilyOwnerAsync(family));
        Assert.Null(await repository.FindLauncherFamilyOwnerAsync(website)); // the website's session is not a launcher's to end
        Assert.Null(await repository.FindLauncherFamilyOwnerAsync(Guid.NewGuid()));
    }
}
