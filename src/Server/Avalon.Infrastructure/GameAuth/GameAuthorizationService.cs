using Avalon.Common.GameAuth;
using System.Globalization;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameTickets;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.GameAuth;

public sealed partial class GameAuthorizationService(IGameContextStore store, AuthAttemptStore attempts, GameAuthCryptography crypto,
    IAccountRepository accounts, IRefreshTokenRepository refreshTokens, IExternalIdentityRepository identities,
    GameProviderRegistry providers, GameLicenseAuthorityService licenseAuthority,
    IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock, IGameAccountRegistration? registration = null, IGameContextRevocations? revocations = null)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private string Key(string kind, string id) => CacheKeys.GameAuth(options.Value.Environment, kind, id);
    private string ContextKey(Guid id) => Key("context", id.ToString("N"));
    private string TokenKey(string secret) => Key("token", GameAuthCryptography.Digest(secret));

    public Task<AuthAttemptReply?> CreateAttemptAsync(string channel, string protocolVersion, Guid clientRunId,
        string linkChallenge, string? contextCredential, uint? steamAppId, CancellationToken cancellationToken)
    {
        var selection = options.Value.ResolveSteamApplication(steamAppId);
        if (selection is null || (channel == GameLaunchChannels.Avalon && selection.AppId != options.Value.SteamAppId) ||
            channel is not (GameLaunchChannels.Avalon or GameLaunchChannels.Steam)) return Task.FromResult<AuthAttemptReply?>(null);
        var key = channel == GameLaunchChannels.Avalon ? "avalon.base" : selection.Restricted ? "steam.playtest" : "steam.main";
        return CreateProviderAttemptAsync(key, protocolVersion, clientRunId, linkChallenge, contextCredential, selection.AppId, cancellationToken);
    }

    public async Task<AuthAttemptReply?> CreateProviderAttemptAsync(string applicationKey, string protocolVersion, Guid clientRunId,
        string linkChallenge, string? contextCredential, uint legacySteamAppId, CancellationToken cancellationToken)
    {
        var application = options.Value.ResolveApplication(applicationKey);
        if (application is null) return null;
        var identityProvider = providers.Identity(application.Provider);
        if (identityProvider is null && providers.License(application.Provider)?.AuthorityKind != LicenseAuthorityKind.StoredGrant) return null;
        GameContextRecord? context = null;
        if (contextCredential is not null)
        {
            context = await GetContextAsync(contextCredential, false, cancellationToken);
            if (context is null || context.ClientRunId != clientRunId || context.ProtocolVersion != protocolVersion ||
                (context.ApplicationKey != application.Key && !(context.LauncherFamilyId is not null && !application.Restricted))) return null;
        }
        var challenge = identityProvider?.CreateChallenge(application) ?? GameAuthCryptography.NewToken();
        return await attempts.CreateAsync(application, protocolVersion, clientRunId, linkChallenge, context?.Id,
            legacySteamAppId, challenge, cancellationToken);
    }

    public async Task<GameContextRecord?> GetContextAsync(string credential, bool requireLicense, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(credential)) return null;
        var token = GameAuthJson.Deserialize<GameAuthTokenRecord>(await store.ReadAsync(TokenKey(credential), cancellationToken));
        if (token is null || token.Kind != GameAuthTokenKinds.Credential || token.Spent) return null;
        var context = GameAuthJson.Deserialize<GameContextRecord>(await store.ReadAsync(ContextKey(token.ContextId), cancellationToken));
        if (context is null || context.Generation != token.Generation || context.CredentialDigest != GameAuthCryptography.Digest(credential) ||
            context.CredentialExpiresAt <= Now || !await IsCurrentAsync(context, cancellationToken)) return null;
        return requireLicense && !await HasCurrentLicenseAsync(context, cancellationToken) ? null : context;
    }

    public async Task<GameContextRecord?> GetContextByIdAsync(Guid id, bool requireLicense, CancellationToken cancellationToken)
    {
        var context = GameAuthJson.Deserialize<GameContextRecord>(await store.ReadAsync(ContextKey(id), cancellationToken));
        return context is not null && await IsCurrentAsync(context, cancellationToken) &&
            (!requireLicense || await HasCurrentLicenseAsync(context, cancellationToken)) ? context : null;
    }

    private bool HasLicense(GameContextRecord context) => context.State == GameAuthStates.Authorized && context.AccountId is not null &&
        context.LicenseId is not null && context.LicenseRevision > 0 && context.AuthorizationValidUntil > Now && context.AbsoluteExpiresAt > Now &&
        (providers.License(context.Provider ?? string.Empty)?.AuthorityKind == LicenseAuthorityKind.StoredGrant ||
         (context.ProviderSubject is not null && context.IdentityValidUntil > Now));

    private async Task<bool> HasCurrentLicenseAsync(GameContextRecord context, CancellationToken ct) => HasLicense(context) &&
        options.Value.ResolveApplication(context.ApplicationKey) is { } application && await licenseAuthority.ValidateAsync(
            context.LicenseId!.Value, context.LicenseRevision!.Value, new AccountId(context.AccountId!.Value), application, Now, ct);

    private async Task<bool> IsCurrentAsync(GameContextRecord context, CancellationToken cancellationToken)
    {
        var application = options.Value.ResolveApplication(context.ApplicationKey);
        if (application is null || context.Provider != application.Provider || context.Environment != options.Value.Environment || context.Audience != GameAuthPolicy.ContextAudience ||
            context.Product != StoreAuthenticationConfiguration.Product || context.State == GameAuthStates.Revoked || context.AbsoluteExpiresAt <= Now ||
            (context.State == GameAuthStates.PendingLink && context.LinkProofExpiresAt <= Now)) return false;
        if (context.AccountId is not { } id) return context.State == GameAuthStates.PendingLink;
        var account = await accounts.FindByIdAsync(new AccountId(id), false, cancellationToken);
        if (!Eligible(account, id) || account!.CredentialsVersion != context.CredentialsVersion || account.SessionEpoch != context.SessionEpoch)
            return false;
        if (providers.Identity(application.Provider) is not null && context.State != GameAuthStates.PendingLink)
        {
            if (context.ProviderSubject is not { } subject) return false;
            var link = await identities.FindAsync(application.Provider, subject, cancellationToken);
            if (link?.AccountId != account.Id) return false;
        }
        if ((context.LicenseId is null) != (context.LicenseRevision is null) ||
            (context.State == GameAuthStates.Authorized && context.LicenseId is null)) return false;
        if (context.LicenseId is { } licenseId && !await licenseAuthority.ValidateBindingAsync(licenseId,
            context.LicenseRevision!.Value, account.Id, application, context.ProviderSubject, cancellationToken)) return false;
        return context.LauncherFamilyId is not { } family ||
            await refreshTokens.IsLiveLauncherFamilyAsync(account.Id, family, Now, cancellationToken);
    }

    private bool Eligible(Account? account, long id) => account is { Status: AccountStatus.Active, GameplayConsolidationId: null } &&
        account.Id.Value == id && (account.AccessLevel & AccountAccessLevel.Player) != 0 && !account.IsLockedAt(Now);

    public async Task<GameAuthReply> RedeemHandoffAsync(string attemptCredential, string ticket, Guid requestId, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(ticket)) return GameAuthReply.Failure(GameAuthErrors.InvalidHandoff);
        var binding = crypto.Binding("handoff", requestId, GameAuthCryptography.Digest(ticket));
        var (claim, prior) = await ClaimAsync(attemptCredential, binding, requestId, ticket, cancellationToken);
        if (prior is not null) return prior;
        if (claim is null) return GameAuthReply.Failure(GameAuthErrors.InvalidAttempt);
        var application = options.Value.ResolveApplication(claim.Record.ApplicationKey);
        if (application?.Provider != StoreProviders.Avalon || application.Key != "avalon.base" || claim.Record.ContextId is not null ||
            !RedisGameTicketStore.TryParseValue(claim.Record.HandoffGrant, true, out var grant))
            return await FinishErrorAsync(claim, GameAuthErrors.InvalidHandoff, cancellationToken);
        var account = await accounts.FindByIdAsync(grant!.AccountId, false, cancellationToken);
        if (!Eligible(account, grant.AccountId.Value) || grant.Environment != options.Value.Environment ||
            account!.CredentialsVersion != grant.CredentialsVersion || account.SessionEpoch != grant.SessionEpoch ||
            !await refreshTokens.IsLiveLauncherFamilyAsync(grant.AccountId, grant.FamilyId, Now, cancellationToken))
            return await FinishErrorAsync(claim, GameAuthErrors.InvalidHandoff, cancellationToken);
        var context = NewContext(claim.Record) with
        {
            State = GameAuthStates.PendingLicense, AccountId = account.Id.Value, CredentialsVersion = account.CredentialsVersion,
            SessionEpoch = account.SessionEpoch, LauncherFamilyId = grant.FamilyId,
        };
        var license = await licenseAuthority.VerifyAsync(new(account.Id, application, null, null, null, Now), cancellationToken);
        context = WithAuthority(context, license);
        return await FinishContextAsync(claim, context, null, null, cancellationToken,
            license.Status == GameLicenseCheckStatus.Unavailable ? GameAuthErrors.ProviderUnavailable : null);
    }

    public Task<GameAuthReply> AuthenticateSteamAsync(string attemptCredential, string ticketHex, Guid requestId,
        CancellationToken cancellationToken, string? sourceAddress = null) =>
        AuthenticateProviderAsync(StoreProviders.Steam, attemptCredential, ticketHex, requestId, cancellationToken, sourceAddress);

    public async Task<GameAuthReply> AuthenticateProviderAsync(string provider, string attemptCredential, string proofValue,
        Guid requestId, CancellationToken cancellationToken, string? sourceAddress = null)
    {
        var identityProvider = providers.Identity(provider);
        if (identityProvider is null || string.IsNullOrEmpty(proofValue) || proofValue.Length > GameAuthPolicy.MaximumBodyBytes)
            return GameAuthReply.Failure(GameAuthErrors.InvalidProof);
        string proofDigest;
        try { proofDigest = crypto.ProviderProofDigest(provider, identityProvider.CanonicalProof(proofValue)); }
        catch (ArgumentException) { return GameAuthReply.Failure(GameAuthErrors.InvalidProof); }
        var binding = crypto.Binding(provider, requestId, proofDigest);
        var (claim, prior) = await ClaimAsync(attemptCredential, binding, requestId, null, cancellationToken);
        if (prior is not null) return prior;
        if (claim is null) return GameAuthReply.Failure(GameAuthErrors.InvalidAttempt);
        var application = options.Value.ResolveApplication(claim.Record.ApplicationKey);
        if (application is null || application.Provider != provider || claim.Record.ProviderChallenge is null)
            return await FinishErrorAsync(claim, GameAuthErrors.InvalidAttempt, cancellationToken);
        GameContextRecord? existing = null;
        if (claim.Record.ContextId is { } contextId)
        {
            existing = await GetContextByIdAsync(contextId, false, cancellationToken);
            if (existing is null || (existing.ApplicationKey != application.Key &&
                !(existing.LauncherFamilyId is not null && !application.Restricted)))
                return await FinishErrorAsync(claim, GameAuthErrors.ContextRevoked, cancellationToken);
        }
        var proof = await identityProvider.VerifyAsync(new(application, claim.Record.ProviderChallenge, proofValue), cancellationToken);
        if (proof.Status == GameIdentityProofStatus.Unavailable)
        {
            await store.CompareExchangeAsync([new(claim.Key, claim.Raw,
                GameAuthJson.Serialize(claim.Record with { WorkerUntil = Now }), claim.Record.ExpiresAt)], cancellationToken);
            return GameAuthReply.Failure(GameAuthErrors.ProviderUnavailable);
        }
        if (proof.Status != GameIdentityProofStatus.Verified || proof.Identity is not { } identity ||
            string.IsNullOrWhiteSpace(identity.ProviderSubject) || identity.ProviderSubject.Length > 128 ||
            identity.VerifiedAt.Kind != DateTimeKind.Utc || identity.ValidUntil.Kind != DateTimeKind.Utc ||
            identity.VerifiedAt > Now || identity.ValidUntil <= Now || identity.ValidUntil > identity.VerifiedAt.Add(GameAuthPolicy.IdentityLifetime))
            return await FinishErrorAsync(claim, GameAuthErrors.InvalidProof, cancellationToken);
        var linked = await identities.FindAsync(provider, identity.ProviderSubject, cancellationToken);
        if (linked is not null && existing?.AccountId is { } expected && linked.AccountId.Value != expected)
            return await FinishErrorAsync(claim, GameAuthErrors.AccountMismatch, cancellationToken, proofDigest);
        var sameSource = existing?.ApplicationKey == application.Key && existing.ProviderSubject == identity.ProviderSubject;
        var context = (existing ?? NewContext(claim.Record)) with
        {
            ApplicationKey = application.Key, Provider = provider, ProviderSubject = identity.ProviderSubject,
            IdentityVerifiedAt = identity.VerifiedAt, IdentityValidUntil = identity.ValidUntil,
            LicenseId = sameSource ? existing!.LicenseId : null, LicenseRevision = sameSource ? existing!.LicenseRevision : null,
            AuthorizationValidUntil = sameSource ? existing!.AuthorizationValidUntil : null,
        };
        if (linked is null && existing?.AccountId is not null)
        {
            context = context with
            {
                State = GameAuthStates.PendingLink, PendingLinkId = Guid.NewGuid(), LinkChallenge = claim.Record.LinkChallenge,
                LinkProofExpiresAt = Earlier(claim.Record.CreatedAt.Add(GameAuthPolicy.LinkProofLifetime), identity.ValidUntil),
                AuthorizationValidUntil = null, LicenseId = null, LicenseRevision = null,
            };
            return await FinishContextAsync(claim, context, existing, proofDigest, cancellationToken, GameAuthErrors.AccountLinkRequired);
        }
        GameLicenseCheckResult? firstEvidence = null;
        if (linked is null)
        {
            var licenseProvider = providers.License(provider);
            if (licenseProvider?.AuthorityKind != LicenseAuthorityKind.VerifiedOwnership)
                return await FinishErrorAsync(claim, GameAuthErrors.ProviderUnavailable, cancellationToken, proofDigest);
            firstEvidence = await licenseProvider.CheckAsync(new(new AccountId(0), application, identity, null, null, Now), cancellationToken);
            if (firstEvidence.Status != GameLicenseCheckStatus.Licensed || firstEvidence.ProviderSubject != identity.ProviderSubject ||
                firstEvidence.ProviderProductId != application.ProviderProductId || firstEvidence.ObservedAt > Now ||
                firstEvidence.AuthorizedUntil <= Now || (firstEvidence.ProviderExpiresAt is { } expiry && expiry <= Now))
                return await FinishErrorAsync(claim, firstEvidence.Status == GameLicenseCheckStatus.Unlicensed
                    ? GameAuthErrors.OwnershipRequired : GameAuthErrors.ProviderUnavailable, cancellationToken, proofDigest);
            if (registration is null || sourceAddress is null)
                return await FinishErrorAsync(claim, GameAuthErrors.ServiceUnavailable, cancellationToken, proofDigest);
            var created = await registration.CreateFromStoreAsync(claim.Record.Id, provider, identity.ProviderSubject,
                Earlier(identity.ValidUntil, Earlier(firstEvidence.ObservedAt.Add(GameAuthPolicy.OwnershipLifetime), firstEvidence.AuthorizedUntil)),
                sourceAddress, cancellationToken);
            linked = created.Status is IdentityLinkStatus.Linked or IdentityLinkStatus.AlreadyLinked ? created.Identity :
                created.Status == IdentityLinkStatus.SubjectTaken ? await identities.FindAsync(provider, identity.ProviderSubject, cancellationToken) : null;
            if (linked is null || linked.Provider != provider || linked.ProviderSubject != identity.ProviderSubject)
                return await FinishErrorAsync(claim, created.Status == IdentityLinkStatus.CreationRefused
                    ? GameAuthErrors.RegistrationLimit : GameAuthErrors.AccountUnavailable, cancellationToken, proofDigest);
        }
        var account = await accounts.FindByIdAsync(linked.AccountId, false, cancellationToken);
        if (!Eligible(account, linked.AccountId.Value))
            return await FinishErrorAsync(claim, GameAuthErrors.AccountUnavailable, cancellationToken, proofDigest);
        context = context with { AccountId = account!.Id.Value, CredentialsVersion = account.CredentialsVersion, SessionEpoch = account.SessionEpoch };
        var license = await licenseAuthority.VerifyAsync(new(account.Id, application, identity, context.LicenseId, context.LicenseRevision, Now),
            cancellationToken, firstEvidence);
        if (license.Status == GameLicenseCheckStatus.Unlicensed && license.LicenseId is not null)
            await PublishRevocationAsync(account.Id, Guid.Empty);
        context = WithAuthority(context, license) with { PendingLinkId = null, LinkChallenge = null, LinkProofExpiresAt = null };
        if (license.Status == GameLicenseCheckStatus.Unavailable && sameSource && existing is not null &&
            await HasCurrentLicenseAsync(existing, cancellationToken))
            context = context with { State = existing.State, AuthorizationValidUntil = existing.AuthorizationValidUntil,
                LicenseId = existing.LicenseId, LicenseRevision = existing.LicenseRevision, LicenseObservationId = existing.LicenseObservationId };
        return await FinishContextAsync(claim, context, existing, proofDigest, cancellationToken,
            license.Status == GameLicenseCheckStatus.Unavailable ? GameAuthErrors.ProviderUnavailable : null);
    }

    private static GameContextRecord WithAuthority(GameContextRecord context, GameLicenseAuthorityResult license) => context with
    {
        State = license.Status == GameLicenseCheckStatus.Licensed ? GameAuthStates.Authorized : GameAuthStates.PendingLicense,
        AuthorizationValidUntil = license.AuthorizedUntil is { } until ? Earlier(until, context.AbsoluteExpiresAt) : null, LicenseObservationId = license.ObservationId,
        LicenseId = license.LicenseId ?? context.LicenseId, LicenseRevision = license.Revision ?? context.LicenseRevision,
    };

    private GameContextRecord NewContext(AuthAttemptRecord attempt) => new()
    {
        Id = Guid.NewGuid(), ClientRunId = attempt.ClientRunId, SteamAppId = attempt.SteamAppId, ProtocolVersion = attempt.ProtocolVersion,
        ApplicationKey = attempt.ApplicationKey, Provider = options.Value.ResolveApplication(attempt.ApplicationKey)?.Provider,
        Environment = options.Value.Environment, State = GameAuthStates.PendingIdentity, CreatedAt = Now, AbsoluteExpiresAt = Now.Add(GameAuthPolicy.AbsoluteContextLifetime),
        CredentialDigest = "", RefreshDigest = "",
    };

    private sealed record Claim(string Key, string Raw, AuthAttemptRecord Record);

    private async Task<(Claim? Claim, GameAuthReply? Prior)> ClaimAsync(string credential, string binding, Guid requestId,
        string? handoff, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(credential) || requestId == Guid.Empty) return (null, null);
        var key = attempts.Key(credential);
        for (var retry = 0; retry < GameAuthPolicy.MutationAttempts; retry++)
        {
            var raw = await store.ReadAsync(key, cancellationToken);
            var record = GameAuthJson.Deserialize<AuthAttemptRecord>(raw);
            if (record is null || options.Value.ResolveApplication(record.ApplicationKey) is null || (record.Binding is not null && record.Binding != binding)) return (null, null);
            var receipt = attempts.Receipt(record, key, binding);
            if (receipt is not null)
            {
                if (receipt.GameContextCredential is not null && await GetContextAsync(receipt.GameContextCredential, false, cancellationToken) is null)
                    return (null, GameAuthReply.Failure(GameAuthErrors.ContextRevoked));
                return (null, receipt);
            }
            if (record.Receipt is not null || record.ExpiresAt <= Now || record.Claims >= 3) return (null, null);
            if (record.WorkerUntil > Now) return (null, GameAuthReply.Failure(GameAuthErrors.InProgress));
            if (handoff is not null && (record.Channel != GameLaunchChannels.Avalon || record.ContextId is not null))
                return (null, GameAuthReply.Failure(GameAuthErrors.InvalidHandoff));
            var claimed = record with { Binding = binding, WorkerId = Guid.NewGuid(), WorkerUntil = Now.Add(GameAuthPolicy.MutationClaimLifetime), Claims = record.Claims + 1 };
            var changes = new List<GameAuthMutation>();
            if (handoff is not null && record.HandoffGrant is null)
            {
                var ticketKey = RedisGameTicketStore.Key(handoff);
                var value = await store.ReadAsync(ticketKey, cancellationToken);
                if (value is null || !RedisGameTicketStore.TryParseValue(value, true, out _)) return (null, GameAuthReply.Failure(GameAuthErrors.InvalidHandoff));
                claimed = claimed with { HandoffGrant = value };
                changes.Add(new(ticketKey, value, null, record.ExpiresAt));
            }
            var next = GameAuthJson.Serialize(claimed);
            changes.Add(new(key, raw, next, record.ExpiresAt));
            if (await store.CompareExchangeAsync(changes, cancellationToken)) return (new(key, next, claimed), null);
        }
        return (null, GameAuthReply.Failure(GameAuthErrors.InProgress));
    }

    private GameAuthMutation FinishMutation(Claim claim, GameAuthReply reply)
    {
        var expires = claim.Record.CreatedAt.Add(GameAuthPolicy.LinkProofLifetime);
        if (reply.ContextExpiresAt is { } credentialEnd && credentialEnd < expires) expires = credentialEnd;
        if (reply.State == GameAuthStates.Authorized && reply.AuthorizationValidUntil is { } licenseEnd && licenseEnd < expires) expires = licenseEnd;
        return new(claim.Key, claim.Raw, GameAuthJson.Serialize(claim.Record with
        {
            Receipt = crypto.Protect(reply, attempts.ReceiptBinding(claim.Key, claim.Record.Binding!)), ReceiptExpiresAt = expires,
            WorkerId = null, WorkerUntil = null,
        }), claim.Record.CreatedAt.Add(GameAuthPolicy.AbsoluteContextLifetime));
    }

    private GameAuthMutation ProofMutation(Claim claim, string digest) =>
        new(Key("proof", digest), null, claim.Record.Id.ToString("N"), claim.Record.CreatedAt.Add(GameAuthPolicy.AbsoluteContextLifetime));

    private async Task<GameAuthReply> FinishErrorAsync(Claim claim, string error, CancellationToken cancellationToken, string? proofDigest = null)
    {
        var reply = GameAuthReply.Failure(error);
        var changes = new List<GameAuthMutation> { FinishMutation(claim, reply) };
        if (proofDigest is not null) changes.Add(ProofMutation(claim, proofDigest));
        return await store.CompareExchangeAsync(changes, cancellationToken) ? reply : GameAuthReply.Failure(GameAuthErrors.InvalidAttempt);
    }

    private async Task<GameAuthReply> FinishContextAsync(Claim claim, GameContextRecord context, GameContextRecord? previous,
        string? proofDigest, CancellationToken cancellationToken, string? error = null)
    {
        var credential = GameAuthCryptography.NewToken();
        var refresh = GameAuthCryptography.NewToken();
        context = context with
        {
            CredentialDigest = GameAuthCryptography.Digest(credential), RefreshDigest = GameAuthCryptography.Digest(refresh),
            CredentialExpiresAt = Earlier(Now.Add(GameAuthPolicy.CredentialLifetime), context.AbsoluteExpiresAt), Generation = (previous?.Generation ?? 0) + 1,
        };
        var contextKey = ContextKey(context.Id);
        var current = await store.ReadAsync(contextKey, cancellationToken);
        if ((previous is null && current is not null) || (previous is not null && current != GameAuthJson.Serialize(previous)))
            return GameAuthReply.Failure(GameAuthErrors.ContextChanged);
        var reply = Response(context, credential, refresh, error);
        var changes = new List<GameAuthMutation>
        {
            FinishMutation(claim, reply), new(contextKey, current, GameAuthJson.Serialize(context), context.AbsoluteExpiresAt),
            new(TokenKey(credential), null, GameAuthJson.Serialize(new GameAuthTokenRecord(context.Id, GameAuthTokenKinds.Credential, context.Generation)), context.CredentialExpiresAt),
            new(TokenKey(refresh), null, GameAuthJson.Serialize(new GameAuthTokenRecord(context.Id, GameAuthTokenKinds.Refresh, context.Generation)), context.AbsoluteExpiresAt),
        };
        if (proofDigest is not null) changes.Add(ProofMutation(claim, proofDigest));
        if (context.PendingLinkId is { } pending)
            changes.Add(new(Key("pending-link", pending.ToString("N")), null, context.Id.ToString("N"), context.LinkProofExpiresAt!.Value));
        if (previous is not null)
        {
            var oldKey = Key("token", previous.RefreshDigest);
            var old = await store.ReadAsync(oldKey, cancellationToken);
            if (old is null) return GameAuthReply.Failure(GameAuthErrors.ContextChanged);
            var retired = GameAuthJson.Deserialize<GameAuthTokenRecord>(old)! with { Spent = true };
            changes.Add(new(oldKey, old, GameAuthJson.Serialize(retired), context.AbsoluteExpiresAt));
        }
        if (!await store.CompareExchangeAsync(changes, cancellationToken)) return GameAuthReply.Failure(GameAuthErrors.ContextChanged);
        return reply;
    }

    private GameAuthReply Response(GameContextRecord context, string credential, string? refresh, string? error = null) => new()
    {
        State = context.State == GameAuthStates.Authorized && !HasLicense(context) ? GameAuthStates.PendingLicense : context.State, Error = error,
        AccountId = context.AccountId?.ToString(CultureInfo.InvariantCulture), GameContextCredential = credential,
        GameContextRefreshToken = refresh, ContextExpiresAt = context.CredentialExpiresAt,
        AuthorizationValidUntil = context.AuthorizationValidUntil, LicenseSource = context.Provider,
        PendingLinkId = context.PendingLinkId?.ToString("N"), NextAction = context.State switch
        {
            GameAuthStates.PendingLink => "link_account", GameAuthStates.Authorized when HasLicense(context) => "select_world",
            _ when error == GameAuthErrors.ProviderUnavailable => "retry_license_check",
            _ when context.Provider is null => "play_through_steam", _ => "purchase_license",
        },
    };

    public async Task<GameAuthReply> RefreshAsync(string refresh, Guid requestId, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(refresh) || requestId == Guid.Empty) return GameAuthReply.Failure(GameAuthErrors.InvalidRefresh);
        var tokenKey = TokenKey(refresh);
        for (var retry = 0; retry < GameAuthPolicy.MutationAttempts; retry++)
        {
            var rawToken = await store.ReadAsync(tokenKey, cancellationToken);
            var token = GameAuthJson.Deserialize<GameAuthTokenRecord>(rawToken);
            if (token is null || token.Kind != GameAuthTokenKinds.Refresh) return GameAuthReply.Failure(GameAuthErrors.InvalidRefresh);
            var contextKey = ContextKey(token.ContextId);
            var rawContext = await store.ReadAsync(contextKey, cancellationToken);
            var context = GameAuthJson.Deserialize<GameContextRecord>(rawContext);
            if (context is null || !await IsCurrentAsync(context, cancellationToken)) return GameAuthReply.Failure(GameAuthErrors.ContextRevoked);
            if (token.Spent)
            {
                if (token.RequestId == requestId && token.Receipt is not null && token.ReceiptExpiresAt > Now)
                {
                    var receipt = crypto.Unprotect(token.Receipt, tokenKey + ":" + requestId.ToString("N"));
                    return receipt.State != GameAuthStates.Authorized || await HasCurrentLicenseAsync(context, cancellationToken)
                        ? receipt : GameAuthReply.Failure(GameAuthErrors.ContextRevoked);
                }
                if (!await store.CompareExchangeAsync([new(contextKey, rawContext,
                    GameAuthJson.Serialize(context with { State = GameAuthStates.Revoked }), context.AbsoluteExpiresAt)], cancellationToken)) continue;
                if (context.AccountId is { } revokedAccount) await PublishRevocationAsync(new AccountId(revokedAccount), context.Id);
                return GameAuthReply.Failure(GameAuthErrors.RefreshReuse);
            }
            if (context.Generation != token.Generation || context.RefreshDigest != GameAuthCryptography.Digest(refresh))
                continue; // Rotation may have committed between the two reads. Re-read the token's exact retry receipt.
            var renewed = context;
            string? renewalError = null;
            var application = options.Value.ResolveApplication(context.ApplicationKey)!;
            if (providers.License(application.Provider)?.AuthorityKind == LicenseAuthorityKind.StoredGrant)
            {
                var license = await licenseAuthority.VerifyAsync(new(new AccountId(context.AccountId!.Value), application,
                    null, context.LicenseId, context.LicenseRevision, Now), cancellationToken);
                renewed = WithAuthority(context, license);
                if (license.Status == GameLicenseCheckStatus.Unavailable)
                {
                    renewalError = GameAuthErrors.ProviderUnavailable;
                    if (await HasCurrentLicenseAsync(context, cancellationToken)) renewed = context;
                }
            }
            var nextCredential = GameAuthCryptography.NewToken();
            var nextRefresh = GameAuthCryptography.NewToken();
            var next = renewed with
            {
                Generation = context.Generation + 1, CredentialDigest = GameAuthCryptography.Digest(nextCredential),
                RefreshDigest = GameAuthCryptography.Digest(nextRefresh), CredentialExpiresAt = Earlier(Now.Add(GameAuthPolicy.CredentialLifetime), context.AbsoluteExpiresAt),
                State = renewed.State == GameAuthStates.Authorized && !HasLicense(renewed) ? GameAuthStates.PendingLicense : renewed.State,
            };
            var reply = Response(next, nextCredential, nextRefresh, renewalError);
            var receiptUntil = Earlier(Now.Add(GameAuthPolicy.RefreshReceiptLifetime), next.CredentialExpiresAt);
            if (next.State == GameAuthStates.Authorized && GameContextAuthorizationWindow.Deadline(next) is { } licenseUntil)
                receiptUntil = Earlier(receiptUntil, licenseUntil);
            var spent = token with { Spent = true, RequestId = requestId,
                Receipt = crypto.Protect(reply, tokenKey + ":" + requestId.ToString("N")), ReceiptExpiresAt = receiptUntil };
            var changes = new GameAuthMutation[]
            {
                new(tokenKey, rawToken, GameAuthJson.Serialize(spent), context.AbsoluteExpiresAt),
                new(contextKey, rawContext, GameAuthJson.Serialize(next), context.AbsoluteExpiresAt),
                new(TokenKey(nextCredential), null, GameAuthJson.Serialize(new GameAuthTokenRecord(context.Id, GameAuthTokenKinds.Credential, next.Generation)), next.CredentialExpiresAt),
                new(TokenKey(nextRefresh), null, GameAuthJson.Serialize(new GameAuthTokenRecord(context.Id, GameAuthTokenKinds.Refresh, next.Generation)), next.AbsoluteExpiresAt),
            };
            if (await store.CompareExchangeAsync(changes, cancellationToken)) return reply;
        }
        return GameAuthReply.Failure(GameAuthErrors.InProgress);
    }

    public async Task<bool> LogoutAsync(string credential, CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(credential, false, cancellationToken);
        if (context is null) return false;
        if (!await store.CompareExchangeAsync([new(ContextKey(context.Id), GameAuthJson.Serialize(context),
            GameAuthJson.Serialize(context with { State = GameAuthStates.Revoked }), context.AbsoluteExpiresAt)], cancellationToken)) return false;
        if (context.AccountId is { } id) await PublishRevocationAsync(new AccountId(id), context.Id);
        return true;
    }

    private Task PublishRevocationAsync(AccountId accountId, Guid contextId) =>
        revocations?.PublishAsync(accountId, contextId) ?? Task.CompletedTask;

    private static DateTime Earlier(DateTime a, DateTime b) => a < b ? a : b;
}
