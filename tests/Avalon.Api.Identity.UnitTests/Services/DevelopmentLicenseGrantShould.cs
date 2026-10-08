using Avalon.Api.Identity.Services;
using Avalon.Api.Testing;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using TestClock = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace Avalon.Api.Identity.UnitTests.Services;

/// <summary>
/// The Development-only license of the seeded admin: granted through the license repository once, found again on the
/// next start, and never written in any other environment. Over a real (SQLite) auth database with its seed.
/// </summary>
public sealed class DevelopmentLicenseGrantShould : IDisposable
{
    private readonly SqliteAuthDatabase _database = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly StoreAuthenticationConfiguration _store = new() { Environment = "development", SteamIdentityPrefix = "avalon-auth-dev" };

    public void Dispose() => _database.Dispose();

    private DevelopmentLicenseGrant Grant(string environmentName)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return new DevelopmentLicenseGrant(environment, new AccountRepository(_database), new GameLicenseRepository(_database),
            Options.Create(_store), _clock, NullLogger<DevelopmentLicenseGrant>.Instance);
    }

    private async Task<List<GameLicense>> LicensesAsync()
    {
        await using AuthDbContext db = await _database.CreateDbContextAsync();
        return await db.GameLicenses.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task Grant_the_seeded_admin_an_active_avalon_license_once_in_Development()
    {
        await Grant(Environments.Development).EnsureAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await Grant(Environments.Development).EnsureAsync(CancellationToken.None);

        GameLicense license = Assert.Single(await LicensesAsync());
        Account admin = (await new AccountRepository(_database).FindByUserNameAsync(DevelopmentLicenseGrant.Username))!;
        Assert.Equal(admin.Id, license.AccountId);
        Assert.Equal(StoreProviders.Avalon, license.Provider);
        Assert.Equal(StoreAuthenticationConfiguration.NativeProviderProduct, license.ProviderProductId);
        Assert.Null(license.ProviderSubject);
        Assert.Equal(LicenseAuthorityKind.StoredGrant, license.AuthorityKind);
        // What admission asks of it: active for the base game in the configured store environment.
        Assert.True(license.Authorizes(admin.Id, StoreAuthenticationConfiguration.Product, _store.Environment,
            _clock.GetUtcNow().UtcDateTime));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Grant_nothing_outside_Development(string environmentName)
    {
        await Grant(environmentName).EnsureAsync(CancellationToken.None);

        Assert.Empty(await LicensesAsync());
    }
}
