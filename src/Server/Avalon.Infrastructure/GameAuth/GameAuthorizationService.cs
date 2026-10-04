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
    ILicenseObservationRepository observations, ISteamProofVerifier verifier, ISteamOwnershipClient ownership,
    IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock, IGameAccountRegistration? registration = null, IGameContextRevocations? revocations = null)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private string Key(string kind, string id) => CacheKeys.GameAuth(options.Value.Environment, kind, id);
    private string ContextKey(Guid id) => Key("context", id.ToString("N"));
    private string TokenKey(string secret) => Key("token", GameAuthCryptography.Digest(secret));

    public async Task<AuthAttemptReply?> CreateAttemptAsync(string channel, string protocolVersion, Guid clientRunId,
        string linkChallenge, string? contextCredential, uint? steamAppId, CancellationToken cancellationToken)
    {
        var selection = options.Value.ResolveSteamApplication(steamAppId);
        if (selection is null || (channel == GameLaunchChannels.Avalon && selection.AppId != options.Value.SteamAppId)) return null;
        GameContextRecord? context = null;
        if (contextCredential is not null)
        {
            context = await GetContextAsync(contextCredential, false, cancellationToken);
            if (context is null || context.ClientRunId != clientRunId || context.ProtocolVersion != protocolVersion || context.SteamAppId != selection.AppId) return null;
        }
        return await attempts.CreateAsync(channel, protocolVersion, clientRunId, linkChallenge, context?.Id, selection.AppId, cancellationToken);
    }

    public async Task<GameContextRecord?> GetContextAsync(string credential, bool requireLicense, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(credential)) return null;
        var token = GameAuthJson.Deserialize<GameAuthTokenRecord>(await store.ReadAsync(TokenKey(credential), cancellationToken));
        if (token is null || token.Kind != GameAuthTokenKinds.Credential || token.Spent) return null;
        var context = GameAuthJson.Deserialize<GameContextRecord>(await store.ReadAsync(ContextKey(token.ContextId), cancellationToken));
        if (context is null || context.Generation != token.Generation || context.CredentialDigest != GameAuthCryptography.Digest(credential) ||
            context.CredentialExpiresAt <= Now || !await IsCurrentAsync(context, cancellationToken)) return null;
        return requireLicense && !HasLicense(context) ? null : context;
    }

    public async Task<GameContextRecord?> GetContextByIdAsync(Guid id, bool requireLicense, CancellationToken cancellationToken)
    {
        var context = GameAuthJson.Deserialize<GameContextRecord>(await store.ReadAsync(ContextKey(id), cancellationToken));
        return context is not null && await IsCurrentAsync(context, cancellationToken) && (!requireLicense || HasLicense(context)) ? context : null;
    }

    private bool HasLicense(GameContextRecord context) => context.State == GameAuthStates.Authorized && context.AccountId is not null &&
        context.Provider == StoreProviders.Steam && context.ProviderSubject is not null && context.AuthorizationValidUntil > Now &&
        context.IdentityVerifiedAt > Now.Subtract(GameAuthPolicy.IdentityLifetime);

    private async Task<bool> IsCurrentAsync(GameContextRecord context, CancellationToken cancellationToken)
    {
        if (options.Value.ResolveSteamApplication(context.SteamAppId) is null || context.Environment != options.Value.Environment || context.Audience != GameAuthPolicy.ContextAudience ||
            context.Product != StoreAuthenticationConfiguration.Product || context.State == GameAuthStates.Revoked || context.AbsoluteExpiresAt <= Now ||
            (context.State == GameAuthStates.PendingLink && context.LinkProofExpiresAt <= Now)) return false;
        if (context.AccountId is not { } id) return context.State == GameAuthStates.PendingLink;
        var account = await accounts.FindByIdAsync(new AccountId(id), false, cancellationToken);
        if (!Eligible(account, id) || account!.CredentialsVersion != context.CredentialsVersion || account.SessionEpoch != context.SessionEpoch)
            return false;
        if (context.Provider == StoreProviders.Steam && context.ProviderSubject is { } subject && context.State != GameAuthStates.PendingLink)
        {
            var link = await identities.FindAsync(StoreProviders.Steam, subject, cancellationToken);
            if (link?.AccountId != account.Id) return false;
            if (context.State == GameAuthStates.Authorized && context.IdentityVerifiedAt is { } verified &&
                await observations.HasNegativeSinceAsync(account.Id, StoreProviders.Steam, subject, context.Environment, context.Product, context.SteamAppId.ToString(CultureInfo.InvariantCulture), verified, cancellationToken)) return false;
        }
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
        if (claim.Record.SteamAppId != options.Value.SteamAppId || claim.Record.Channel != GameLaunchChannels.Avalon || claim.Record.ContextId is not null ||
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
        return await FinishContextAsync(claim, context, null, null, cancellationToken);
    }

    public async Task<GameAuthReply> AuthenticateSteamAsync(string attemptCredential, string ticketHex, Guid requestId,
        CancellationToken cancellationToken, string? sourceAddress = null)
    {
        if (string.IsNullOrEmpty(ticketHex) || ticketHex.Length > GameAuthPolicy.MaximumSteamTicketHexCharacters || ticketHex.Length % 2 != 0 || !ticketHex.All(Uri.IsHexDigit))
            return GameAuthReply.Failure(GameAuthErrors.InvalidProof);
        var proofDigest = crypto.ProofDigest(ticketHex);
        var binding = crypto.Binding(StoreProviders.Steam, requestId, proofDigest);
        var (claim, prior) = await ClaimAsync(attemptCredential, binding, requestId, null, cancellationToken);
        if (prior is not null) return prior;
        if (claim is null) return GameAuthReply.Failure(GameAuthErrors.InvalidAttempt);
        GameContextRecord? existing = null;
        if (claim.Record.ContextId is { } contextId)
        {
            existing = await GetContextByIdAsync(contextId, false, cancellationToken);
            if (existing is null || existing.SteamAppId != claim.Record.SteamAppId) return await FinishErrorAsync(claim, GameAuthErrors.ContextRevoked, cancellationToken);
        }
        var proof = await verifier.VerifyAsync(claim.Record.SteamAppId, ticketHex, claim.Record.ExpectedSteamIdentity, cancellationToken);
        if (proof.Status == SteamProofStatus.ProviderUnavailable)
        {
            await store.CompareExchangeAsync([new(claim.Key, claim.Raw,
                GameAuthJson.Serialize(claim.Record with { WorkerUntil = Now }), claim.Record.ExpiresAt)], cancellationToken);
            return GameAuthReply.Failure(GameAuthErrors.ProviderUnavailable);
        }
        if (proof.Status != SteamProofStatus.Verified || proof.ProviderSubject is null)
            return await FinishErrorAsync(claim, GameAuthErrors.InvalidProof, cancellationToken);
        var linked = await identities.FindAsync(StoreProviders.Steam, proof.ProviderSubject, cancellationToken);
        if (linked is not null && existing?.AccountId is { } expected && linked.AccountId.Value != expected)
            return await FinishErrorAsync(claim, GameAuthErrors.AccountMismatch, cancellationToken, proofDigest);
        var context = existing ?? NewContext(claim.Record);
        context = context with { Provider = StoreProviders.Steam, ProviderSubject = proof.ProviderSubject, IdentityVerifiedAt = Now };
        if (linked is null && existing?.AccountId is not null)
        {
            context = context with
            {
                State = GameAuthStates.PendingLink, PendingLinkId = Guid.NewGuid(), LinkChallenge = claim.Record.LinkChallenge,
                LinkProofExpiresAt = claim.Record.CreatedAt.Add(GameAuthPolicy.LinkProofLifetime), AuthorizationValidUntil = null,
            };
            return await FinishContextAsync(claim, context, existing, proofDigest, cancellationToken, GameAuthErrors.AccountLinkRequired);
        }
        SteamOwnershipResult? firstLicense = null;
        if (linked is null)
        {
            firstLicense = await ownership.CheckAsync(claim.Record.SteamAppId, proof.ProviderSubject, cancellationToken);
            if (firstLicense.Status != SteamOwnershipStatus.Owned || firstLicense.ProviderSubject != proof.ProviderSubject ||
                firstLicense.AuthorizedUntil <= Now || firstLicense.AuthorizedUntil > firstLicense.ObservedAt.Add(GameAuthPolicy.OwnershipLifetime))
                return await FinishErrorAsync(claim, firstLicense.Status == SteamOwnershipStatus.NotOwned
                    ? GameAuthErrors.OwnershipRequired : GameAuthErrors.ProviderUnavailable, cancellationToken, proofDigest);
            if (registration is null || sourceAddress is null)
                return await FinishErrorAsync(claim, GameAuthErrors.ServiceUnavailable, cancellationToken, proofDigest);
            var created = await registration.CreateFromSteamAsync(claim.Record.Id, proof.ProviderSubject,
                Earlier(claim.Record.CreatedAt.Add(GameAuthPolicy.LinkProofLifetime), firstLicense.AuthorizedUntil), sourceAddress, cancellationToken);
            linked = created.Status is IdentityLinkStatus.Linked or IdentityLinkStatus.AlreadyLinked ? created.Identity :
                created.Status == IdentityLinkStatus.SubjectTaken ? await identities.FindAsync(StoreProviders.Steam, proof.ProviderSubject, cancellationToken) : null;
            if (linked is null)
                return await FinishErrorAsync(claim, created.Status == IdentityLinkStatus.CreationRefused
                    ? GameAuthErrors.RegistrationLimit : GameAuthErrors.AccountUnavailable, cancellationToken, proofDigest);
        }
        var account = await accounts.FindByIdAsync(linked.AccountId, false, cancellationToken);
        if (!Eligible(account, linked.AccountId.Value))
            return await FinishErrorAsync(claim, GameAuthErrors.AccountUnavailable, cancellationToken, proofDigest);
        context = context with { AccountId = account!.Id.Value, CredentialsVersion = account.CredentialsVersion, SessionEpoch = account.SessionEpoch };
        var license = firstLicense ?? await ownership.CheckAsync(claim.Record.SteamAppId, proof.ProviderSubject, cancellationToken);
        if (license.ProviderSubject != proof.ProviderSubject || license.AuthorizedUntil > license.ObservedAt.Add(GameAuthPolicy.OwnershipLifetime))
            return await FinishErrorAsync(claim, GameAuthErrors.ProviderUnavailable, cancellationToken, proofDigest);
        Guid? observationId = null;
        if (license.Status != SteamOwnershipStatus.ProviderUnavailable)
        {
            observationId = Guid.NewGuid();
            await observations.RecordAsync(new LicenseObservation
            {
                Id = observationId.Value, AccountId = account.Id, Provider = StoreProviders.Steam, ProviderSubject = proof.ProviderSubject,
                ProviderOwnerSubject = license.OwnerSubject, Permanent = license.Permanent, OwnsProduct = license.Status == SteamOwnershipStatus.Owned,
                Environment = options.Value.Environment, Product = StoreAuthenticationConfiguration.Product,
                ProviderAppId = claim.Record.SteamAppId.ToString(CultureInfo.InvariantCulture), ObservedAt = license.ObservedAt,
                AuthorizedUntil = license.AuthorizedUntil, ProviderExpiresAt = license.ProviderExpiresAt, PolicyVersion = options.Value.PolicyVersion,
            }, cancellationToken);
            // The observation is durable even if a later Redis receipt write loses its race.
            if (license.Status == SteamOwnershipStatus.NotOwned) await PublishRevocationAsync(account.Id, Guid.Empty);
        }
        context = context with
        {
            State = license.Status == SteamOwnershipStatus.Owned && license.AuthorizedUntil > Now ? GameAuthStates.Authorized : GameAuthStates.PendingLicense,
            AuthorizationValidUntil = license.Status == SteamOwnershipStatus.Owned ? license.AuthorizedUntil : null,
            LicenseObservationId = observationId, PendingLinkId = null, LinkChallenge = null, LinkProofExpiresAt = null,
        };
        // Provider outage does not erase an already granted deadline, nor extend it.
        if (license.Status == SteamOwnershipStatus.ProviderUnavailable && existing is { AuthorizationValidUntil: not null })
            context = context with { State = existing.AuthorizationValidUntil > Now ? GameAuthStates.Authorized : GameAuthStates.PendingLicense,
                AuthorizationValidUntil = existing.AuthorizationValidUntil, LicenseObservationId = existing.LicenseObservationId };
        return await FinishContextAsync(claim, context, existing, proofDigest, cancellationToken,
            license.Status == SteamOwnershipStatus.ProviderUnavailable ? GameAuthErrors.ProviderUnavailable : null);
    }

    private GameContextRecord NewContext(AuthAttemptRecord attempt) => new()
    {
        Id = Guid.NewGuid(), ClientRunId = attempt.ClientRunId, SteamAppId = attempt.SteamAppId, ProtocolVersion = attempt.ProtocolVersion,
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
            if (record is null || options.Value.ResolveSteamApplication(record.SteamAppId) is null || (record.Binding is not null && record.Binding != binding)) return (null, null);
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
        AuthorizationValidUntil = context.AuthorizationValidUntil, LicenseSource = context.AuthorizationValidUntil is not null ? context.Provider : null,
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
                    return crypto.Unprotect(token.Receipt, tokenKey + ":" + requestId.ToString("N"));
                if (!await store.CompareExchangeAsync([new(contextKey, rawContext,
                    GameAuthJson.Serialize(context with { State = GameAuthStates.Revoked }), context.AbsoluteExpiresAt)], cancellationToken)) continue;
                if (context.AccountId is { } revokedAccount) await PublishRevocationAsync(new AccountId(revokedAccount), context.Id);
                return GameAuthReply.Failure(GameAuthErrors.RefreshReuse);
            }
            if (context.Generation != token.Generation || context.RefreshDigest != GameAuthCryptography.Digest(refresh))
                continue; // Rotation may have committed between the two reads. Re-read the token's exact retry receipt.
            var nextCredential = GameAuthCryptography.NewToken();
            var nextRefresh = GameAuthCryptography.NewToken();
            var next = context with
            {
                Generation = context.Generation + 1, CredentialDigest = GameAuthCryptography.Digest(nextCredential),
                RefreshDigest = GameAuthCryptography.Digest(nextRefresh), CredentialExpiresAt = Earlier(Now.Add(GameAuthPolicy.CredentialLifetime), context.AbsoluteExpiresAt),
                State = context.State == GameAuthStates.Authorized && !HasLicense(context) ? GameAuthStates.PendingLicense : context.State,
            };
            var reply = Response(next, nextCredential, nextRefresh);
            var spent = token with { Spent = true, RequestId = requestId,
                Receipt = crypto.Protect(reply, tokenKey + ":" + requestId.ToString("N")), ReceiptExpiresAt = Earlier(Now.Add(GameAuthPolicy.RefreshReceiptLifetime), context.AbsoluteExpiresAt) };
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
