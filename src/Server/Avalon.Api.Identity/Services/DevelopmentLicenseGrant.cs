using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Services;

/// <summary>
/// Development only: makes sure the seeded <see cref="Username"/> account holds an active Avalon stored grant for the
/// base game in the configured store environment, so a fresh local database can enter a world without a purchase
/// (only fulfilment writes grants anywhere else). Identity runs it at startup, after the auth schema is migrated; in
/// any environment but Development it does nothing at all. The grant goes through
/// <see cref="IGameLicenseRepository.RecordGrantAsync"/> under one fixed reference per account, so a second start
/// finds it and writes nothing. A grant that was revoked or suspended since is left as it is, with a warning: it is
/// never re-granted.
/// </summary>
public sealed class DevelopmentLicenseGrant(IHostEnvironment environment, IAccountRepository accounts,
    IGameLicenseRepository licenses, IOptions<StoreAuthenticationConfiguration> store, TimeProvider clock,
    ILogger<DevelopmentLicenseGrant> logger)
{
    /// <summary>The account the auth migrations seed (password 123, Admin), the only one granted.</summary>
    public const string Username = "ADMIN";

    /// <summary>The grant's reference: one per account, so a restart finds the grant it made.</summary>
    public static string Reference(long accountId) => $"development:{accountId}";

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) return;

        Account? account = await accounts.FindByUserNameAsync(Username, cancellationToken);
        if (account is null)
        {
            logger.LogWarning("No {Username} account to grant a development license to", Username);
            return;
        }

        string licenseEnvironment = store.Value.Environment;
        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (await licenses.FindActiveAsync(account.Id, StoreProviders.Avalon, licenseEnvironment,
                StoreAuthenticationConfiguration.Product, StoreAuthenticationConfiguration.NativeProviderProduct, now,
                cancellationToken) is not null)
        {
            return;
        }

        GameLicense grant = await licenses.RecordGrantAsync(new GameLicense
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Provider = StoreProviders.Avalon,
            Environment = licenseEnvironment,
            Product = StoreAuthenticationConfiguration.Product,
            ProviderProductId = StoreAuthenticationConfiguration.NativeProviderProduct,
            LicenseReference = Reference(account.Id.Value),
            AuthorityKind = LicenseAuthorityKind.StoredGrant,
            GrantedAt = now,
        }, cancellationToken);

        if (grant.Authorizes(account.Id, StoreAuthenticationConfiguration.Product, licenseEnvironment, now))
        {
            logger.LogInformation(
                "Development: granted account {Username} ({AccountId}) the {Product} license in store environment {LicenseEnvironment}",
                Username, account.Id.Value, StoreAuthenticationConfiguration.Product, licenseEnvironment);
        }
        else
        {
            logger.LogWarning(
                "Development: the {Product} license of account {Username} ({AccountId}) in store environment {LicenseEnvironment} " +
                "is revoked or suspended and is not granted again",
                StoreAuthenticationConfiguration.Product, Username, account.Id.Value, licenseEnvironment);
        }
    }
}
