using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.GameAuth;

public sealed record GameLicenseAuthorityResult(GameLicenseCheckStatus Status, Guid? LicenseId = null,
    long? Revision = null, DateTime? AuthorizedUntil = null, Guid? ObservationId = null);

/// <summary>Owns common source/revision binding and bounded authority; adapters cannot issue game contexts.</summary>
public sealed class GameLicenseAuthorityService(GameProviderRegistry providers, IGameLicenseRepository licenses,
    ILicenseObservationRepository observations, IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock)
{
    private static GameLicenseAuthorityResult Unavailable => new(GameLicenseCheckStatus.Unavailable);
    private static GameLicenseAuthorityResult Unlicensed => new(GameLicenseCheckStatus.Unlicensed);

    public async Task<GameLicenseAuthorityResult> VerifyAsync(GameLicenseCheckRequest request, CancellationToken ct,
        GameLicenseCheckResult? verifiedEvidence = null)
    {
        if (!Trusted(request.Application) || request.Account is null || request.Account.Value <= 0 ||
            request.Now.Kind != DateTimeKind.Utc || (request.BoundLicenseId is null) != (request.BoundRevision is null))
        {
            return Unavailable;
        }

        IGameLicenseProvider? provider = providers.License(request.Application.Provider);
        if (provider is null || !ValidIdentity(provider.AuthorityKind, request.Identity, request.Now)) return Unavailable;
        try
        {
            GameLicense? license = request.BoundLicenseId is { } bound ? await licenses.FindAsync(bound, ct) : null;
            if (request.BoundLicenseId is not null && (license is null || !Matches(license, request, provider.AuthorityKind) ||
                license.AuthorityRevision != request.BoundRevision || license.RevokedAt is not null || license.SuspendedAt is not null))
            {
                return Unlicensed;
            }

            GameLicenseCheckResult evidence = verifiedEvidence ?? await provider.CheckAsync(request, ct);
            if (evidence.Status == GameLicenseCheckStatus.Unavailable) return Unavailable;
            if (evidence.Status == GameLicenseCheckStatus.Unlicensed && request.BoundLicenseId is null &&
                evidence.LicenseId is null && string.IsNullOrEmpty(evidence.LicenseReference))
            {
                return Unlicensed;
            }

            DateTime completedAt = clock.GetUtcNow().UtcDateTime;
            if (completedAt < request.Now || !ValidIdentity(provider.AuthorityKind, request.Identity, completedAt) ||
                !ValidEvidence(evidence, request, completedAt))
            {
                return Unavailable;
            }

            license ??= await licenses.FindAsync(request.Account, request.Application.Provider, request.Application.Environment, evidence.LicenseReference, ct);
            if (evidence.LicenseId is { } selected && license?.Id != selected) return Unavailable;
            if (license is not null && (!Matches(license, request, provider.AuthorityKind) || license.LicenseReference != evidence.LicenseReference)) return Unavailable;
            bool owns = evidence.Status == GameLicenseCheckStatus.Licensed;
            // Expiry of a fulfilled grant is not a new revocation. Revocation is stored by fulfillment.
            if (!owns && provider.AuthorityKind == LicenseAuthorityKind.StoredGrant) return Unlicensed;
            DateTime end = owns ? Earlier(evidence.AuthorizedUntil, evidence.ObservedAt.Add(GameAuthPolicy.OwnershipLifetime)) : evidence.ObservedAt;
            if (evidence.ProviderExpiresAt is { } expiry) end = Earlier(end, expiry);
            if (owns && request.Identity is { } identity) end = Earlier(end, identity.ValidUntil);
            if (owns && end <= completedAt) return Unavailable;
            if (license is null)
            {
                // Stored grants come only from fulfillment; identity proof cannot manufacture one.
                if (!owns || provider.AuthorityKind == LicenseAuthorityKind.StoredGrant) return Unlicensed;
                license = await licenses.RecordGrantAsync(new GameLicense
                {
                    Id = Guid.NewGuid(),
                    AccountId = request.Account,
                    Product = request.Application.Product,
                    Provider = request.Application.Provider,
                    Environment = request.Application.Environment,
                    ProviderProductId = request.Application.ProviderProductId,
                    ProviderSubject = request.Identity!.ProviderSubject,
                    LicenseReference = evidence.LicenseReference,
                    AuthorityKind = provider.AuthorityKind,
                    GrantedAt = evidence.ObservedAt,
                    ExpiresAt = evidence.ProviderExpiresAt,
                }, ct);
                if (!Matches(license, request, provider.AuthorityKind)) return Unavailable;
            }
            if (owns && license.SuspendedAt is not null) return Unlicensed;
            if (owns && license.AuthorityKind == LicenseAuthorityKind.StoredGrant && !license.Authorizes(request.Account,
                request.Application.Product, request.Application.Environment, completedAt))
            {
                return Unlicensed;
            }

            if (owns && license.ExpiresAt is { } storedExpiry && license.AuthorityKind == LicenseAuthorityKind.StoredGrant) end = Earlier(end, storedExpiry);
            if (owns && end <= completedAt) return Unavailable;
            GameLicense? applied = await licenses.ApplyDecisionAsync(license.Id, license.AuthorityRevision,
                new(owns, evidence.ObservedAt, end, evidence.ProviderExpiresAt,
                    reestablish: request.BoundLicenseId is null && provider.AuthorityKind == LicenseAuthorityKind.VerifiedOwnership), ct);
            if (applied is null) return Unlicensed;
            if (!Matches(applied, request, provider.AuthorityKind)) return Unavailable;
            if (owns && !applied.Authorizes(request.Account, request.Application.Product, request.Application.Environment, completedAt)) return Unlicensed;
            var observation = new LicenseObservation
            {
                Id = Guid.NewGuid(),
                LicenseId = applied.Id,
                AuthorityRevision = applied.AuthorityRevision,
                AccountId = request.Account,
                Provider = request.Application.Provider,
                ProviderSubject = request.Identity?.ProviderSubject ?? string.Empty,
                Environment = request.Application.Environment,
                Product = request.Application.Product,
                ProviderProductId = request.Application.ProviderProductId,
                ProviderOwnerSubject = evidence.OwnerSubject,
                Permanent = evidence.Permanent,
                OwnsProduct = owns,
                ObservedAt = evidence.ObservedAt,
                AuthorizedUntil = end,
                ProviderExpiresAt = evidence.ProviderExpiresAt,
                PolicyVersion = options.Value.PolicyVersion,
            };
            await observations.RecordAsync(observation, ct);
            return owns ? new(GameLicenseCheckStatus.Licensed, applied.Id, applied.AuthorityRevision, end, observation.Id)
                : new(GameLicenseCheckStatus.Unlicensed, applied.Id, applied.AuthorityRevision, null, observation.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable; }
    }

    public async Task<bool> ValidateAsync(Guid licenseId, long revision, AccountId account,
        GameApplicationSelection application, DateTime now, CancellationToken ct) =>
        await CheckCurrentAsync(licenseId, revision, account, application, now, ct) == true;

    /// <summary>
    /// <see cref="ValidateAsync"/>, telling an outage apart: null when the license could not be read, so a caller that
    /// would act on a refusal (revoke a context) does not act on a failed read.
    /// </summary>
    public async Task<bool?> CheckCurrentAsync(Guid licenseId, long revision, AccountId account,
        GameApplicationSelection application, DateTime now, CancellationToken ct)
    {
        IGameLicenseProvider? provider = providers.License(application.Provider);
        if (!Trusted(application) || provider is null || now.Kind != DateTimeKind.Utc || revision <= 0) return false;
        try
        {
            GameLicense? license = await licenses.FindAsync(licenseId, ct);
            return license is not null && license.AuthorityRevision == revision && license.Provider == application.Provider &&
                license.ProviderProductId == application.ProviderProductId && license.AuthorityKind == provider.AuthorityKind &&
                license.Authorizes(account, application.Product, application.Environment, now);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Whether the license row still holds the binding a context was issued with; null when the row could not be read
    /// (an outage is not a refusal, so a caller answers it as unavailable rather than revoked).
    /// </summary>
    public async Task<bool?> ValidateBindingAsync(Guid licenseId, long revision, AccountId account,
        GameApplicationSelection application, string? subject, CancellationToken ct)
    {
        IGameLicenseProvider? provider = providers.License(application.Provider);
        if (!Trusted(application) || provider is null || revision <= 0) return false;
        try
        {
            GameLicense? row = await licenses.FindAsync(licenseId, ct);
            return row is not null && row.Id == licenseId && row.AuthorityRevision == revision && row.RevokedAt is null && row.SuspendedAt is null &&
                row.AccountId == account && row.Product == application.Product && row.Environment == application.Environment &&
                row.Provider == application.Provider && row.ProviderProductId == application.ProviderProductId &&
                row.ProviderSubject == subject && row.AuthorityKind == provider.AuthorityKind;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    private bool Trusted(GameApplicationSelection app)
    {
        GameApplicationSelection? configured = options.Value.ResolveApplication(app.Key);
        return configured is not null && configured.Provider == app.Provider && configured.ProviderProductId == app.ProviderProductId &&
            configured.Product == app.Product && configured.Environment == app.Environment && configured.Restricted == app.Restricted &&
            configured.AllowedWorldIds.SequenceEqual(app.AllowedWorldIds);
    }
    private static bool ValidIdentity(LicenseAuthorityKind kind, VerifiedGameIdentity? identity, DateTime now) =>
        kind == LicenseAuthorityKind.StoredGrant ? identity is null : identity is not null && Text(identity.ProviderSubject, 128) &&
            identity.VerifiedAt.Kind == DateTimeKind.Utc && identity.ValidUntil.Kind == DateTimeKind.Utc && identity.VerifiedAt <= now &&
            identity.ValidUntil > now && identity.ValidUntil <= identity.VerifiedAt.Add(GameAuthPolicy.IdentityLifetime);
    private static bool ValidEvidence(GameLicenseCheckResult evidence, GameLicenseCheckRequest request, DateTime completedAt) =>
        evidence.Status is GameLicenseCheckStatus.Licensed or GameLicenseCheckStatus.Unlicensed && Text(evidence.LicenseReference, 256) &&
        evidence.ProviderProductId == request.Application.ProviderProductId && evidence.ProviderSubject == request.Identity?.ProviderSubject &&
        evidence.ObservedAt.Kind == DateTimeKind.Utc && evidence.ObservedAt <= completedAt &&
        evidence.AuthorizedUntil.Kind == DateTimeKind.Utc && (evidence.ProviderExpiresAt is null || evidence.ProviderExpiresAt.Value.Kind == DateTimeKind.Utc) &&
        (evidence.Status != GameLicenseCheckStatus.Licensed || evidence.AuthorizedUntil > evidence.ObservedAt);
    private static bool Matches(GameLicense row, GameLicenseCheckRequest request, LicenseAuthorityKind kind) =>
        row.AccountId == request.Account && row.Provider == request.Application.Provider && row.Environment == request.Application.Environment &&
        row.Product == request.Application.Product && row.ProviderProductId == request.Application.ProviderProductId &&
        row.ProviderSubject == request.Identity?.ProviderSubject && row.AuthorityKind == kind;
    private static bool Text(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    private static DateTime Earlier(DateTime one, DateTime two) => one < two ? one : two;
}
