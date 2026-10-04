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
    IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private string Key(string kind, string id) => CacheKeys.GameAuth(options.Value.Environment, kind, id);
    private string ContextKey(Guid id) => Key("context", id.ToString("N"));
    private string TokenKey(string secret) => Key("token", GameAuthCryptography.Digest(secret));

    public async Task<AuthAttemptReply?> CreateAttemptAsync(string channel, string protocolVersion, Guid clientRunId,
        string linkChallenge, string? contextCredential, CancellationToken cancellationToken)
    {
        GameContextRecord? context = null;
        if (contextCredential is not null)
        {
            context = await GetContextAsync(contextCredential, false, cancellationToken);
            if (context is null || context.ClientRunId != clientRunId || context.ProtocolVersion != protocolVersion) return null;
        }
        return await attempts.CreateAsync(channel, protocolVersion, clientRunId, linkChallenge, context?.Id, cancellationToken);
    }

    public async Task<GameContextRecord?> GetContextAsync(string credential, bool requireLicense, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(credential)) return null;
        var token = GameAuthJson.Deserialize<GameAuthTokenRecord>(await store.ReadAsync(TokenKey(credential), cancellationToken));
        if (token is null || token.Kind != "credential" || token.Spent) return null;
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

    private bool HasLicense(GameContextRecord context) => context.State == "authorized" && context.AccountId is not null &&
        context.Provider == "steam" && context.ProviderSubject is not null && context.AuthorizationValidUntil > Now &&
        context.IdentityVerifiedAt > Now.AddMinutes(-30);

    private async Task<bool> IsCurrentAsync(GameContextRecord context, CancellationToken cancellationToken)
    {
        if (context.Environment != options.Value.Environment || context.Audience != "avalon.game-auth" ||
            context.Product != StoreAuthenticationConfiguration.Product || context.State == "revoked" || context.AbsoluteExpiresAt <= Now ||
            (context.State == "pending_link" && context.LinkProofExpiresAt <= Now)) return false;
        if (context.AccountId is not { } id) return context.State == "pending_link";
        var account = await accounts.FindByIdAsync(new AccountId(id), false, cancellationToken);
        if (!Eligible(account, id) || account!.CredentialsVersion != context.CredentialsVersion || account.SessionEpoch != context.SessionEpoch)
            return false;
        return context.LauncherFamilyId is not { } family ||
            await refreshTokens.IsLiveLauncherFamilyAsync(account.Id, family, Now, cancellationToken);
    }

    private bool Eligible(Account? account, long id) => account is { Status: AccountStatus.Active } &&
        account.Id.Value == id && (account.AccessLevel & AccountAccessLevel.Player) != 0 && !account.IsLockedAt(Now);

    public async Task<GameAuthReply> RedeemHandoffAsync(string attemptCredential, string ticket, Guid requestId, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(ticket)) return GameAuthReply.Failure("INVALID_HANDOFF");
        var binding = crypto.Binding("handoff", requestId, GameAuthCryptography.Digest(ticket));
        var (claim, prior) = await ClaimAsync(attemptCredential, binding, requestId, ticket, cancellationToken);
        if (prior is not null) return prior;
        if (claim is null) return GameAuthReply.Failure("INVALID_ATTEMPT");
        if (claim.Record.Channel != "avalon" || claim.Record.ContextId is not null ||
            !RedisGameTicketStore.TryParseValue(claim.Record.HandoffGrant, true, out var grant))
            return await FinishErrorAsync(claim, "INVALID_HANDOFF", cancellationToken);
        var account = await accounts.FindByIdAsync(grant!.AccountId, false, cancellationToken);
        if (!Eligible(account, grant.AccountId.Value) || grant.Environment != options.Value.Environment ||
            account!.CredentialsVersion != grant.CredentialsVersion || account.SessionEpoch != grant.SessionEpoch ||
            !await refreshTokens.IsLiveLauncherFamilyAsync(grant.AccountId, grant.FamilyId, Now, cancellationToken))
            return await FinishErrorAsync(claim, "INVALID_HANDOFF", cancellationToken);
        var context = NewContext(claim.Record) with
        {
            State = "pending_license", AccountId = account.Id.Value, CredentialsVersion = account.CredentialsVersion,
            SessionEpoch = account.SessionEpoch, LauncherFamilyId = grant.FamilyId,
        };
        return await FinishContextAsync(claim, context, null, null, cancellationToken);
    }

    public async Task<GameAuthReply> AuthenticateSteamAsync(string attemptCredential, string ticketHex, Guid requestId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(ticketHex) || ticketHex.Length > 5120 || ticketHex.Length % 2 != 0 || !ticketHex.All(Uri.IsHexDigit))
            return GameAuthReply.Failure("INVALID_PROOF");
        var proofDigest = crypto.ProofDigest(ticketHex);
        var binding = crypto.Binding("steam", requestId, proofDigest);
        var (claim, prior) = await ClaimAsync(attemptCredential, binding, requestId, null, cancellationToken);
        if (prior is not null) return prior;
        if (claim is null) return GameAuthReply.Failure("INVALID_ATTEMPT");
        GameContextRecord? existing = null;
        if (claim.Record.ContextId is { } contextId)
        {
            existing = await GetContextByIdAsync(contextId, false, cancellationToken);
            if (existing is null) return await FinishErrorAsync(claim, "CONTEXT_REVOKED", cancellationToken);
        }
        var proof = await verifier.VerifyAsync(ticketHex, claim.Record.ExpectedSteamIdentity, cancellationToken);
        if (proof.Status == SteamProofStatus.ProviderUnavailable)
        {
            await store.CompareExchangeAsync([new(claim.Key, claim.Raw,
                GameAuthJson.Serialize(claim.Record with { WorkerUntil = Now }), claim.Record.ExpiresAt)], cancellationToken);
            return GameAuthReply.Failure("PROVIDER_UNAVAILABLE");
        }
        if (proof.Status != SteamProofStatus.Verified || proof.ProviderSubject is null)
            return await FinishErrorAsync(claim, "INVALID_PROOF", cancellationToken);
        var linked = await identities.FindAsync("steam", proof.ProviderSubject, cancellationToken);
        if (linked is not null && existing?.AccountId is { } expected && linked.AccountId.Value != expected)
            return await FinishErrorAsync(claim, "ACCOUNT_MISMATCH", cancellationToken, proofDigest);
        var context = existing ?? NewContext(claim.Record);
        context = context with { Provider = "steam", ProviderSubject = proof.ProviderSubject, IdentityVerifiedAt = Now };
        if (linked is null)
        {
            context = context with
            {
                State = "pending_link", PendingLinkId = Guid.NewGuid(), LinkChallenge = claim.Record.LinkChallenge,
                LinkProofExpiresAt = claim.Record.CreatedAt.AddMinutes(5), AuthorizationValidUntil = null,
            };
            return await FinishContextAsync(claim, context, existing, proofDigest, cancellationToken, "ACCOUNT_LINK_REQUIRED");
        }
        var account = await accounts.FindByIdAsync(linked.AccountId, false, cancellationToken);
        if (!Eligible(account, linked.AccountId.Value))
            return await FinishErrorAsync(claim, "ACCOUNT_UNAVAILABLE", cancellationToken, proofDigest);
        context = context with { AccountId = account!.Id.Value, CredentialsVersion = account.CredentialsVersion, SessionEpoch = account.SessionEpoch };
        var license = await ownership.CheckAsync(proof.ProviderSubject, cancellationToken);
        if (license.ProviderSubject != proof.ProviderSubject || license.AuthorizedUntil > license.ObservedAt.AddMinutes(5))
            return await FinishErrorAsync(claim, "PROVIDER_UNAVAILABLE", cancellationToken, proofDigest);
        Guid? observationId = null;
        if (license.Status != SteamOwnershipStatus.ProviderUnavailable)
        {
            observationId = Guid.NewGuid();
            await observations.RecordAsync(new LicenseObservation
            {
                Id = observationId.Value, AccountId = account.Id, Provider = "steam", ProviderSubject = proof.ProviderSubject,
                ProviderOwnerSubject = license.OwnerSubject, Permanent = license.Permanent, OwnsProduct = license.Status == SteamOwnershipStatus.Owned,
                Environment = options.Value.Environment, Product = StoreAuthenticationConfiguration.Product,
                ProviderAppId = options.Value.SteamAppId.ToString(CultureInfo.InvariantCulture), ObservedAt = license.ObservedAt,
                AuthorizedUntil = license.AuthorizedUntil, ProviderExpiresAt = license.ProviderExpiresAt, PolicyVersion = options.Value.PolicyVersion,
            }, cancellationToken);
        }
        context = context with
        {
            State = license.Status == SteamOwnershipStatus.Owned && license.AuthorizedUntil > Now ? "authorized" : "pending_license",
            AuthorizationValidUntil = license.Status == SteamOwnershipStatus.Owned ? license.AuthorizedUntil : null,
            LicenseObservationId = observationId, PendingLinkId = null, LinkChallenge = null, LinkProofExpiresAt = null,
        };
        // Provider outage does not erase an already granted deadline, nor extend it.
        if (license.Status == SteamOwnershipStatus.ProviderUnavailable && existing is { AuthorizationValidUntil: not null })
            context = context with { State = existing.AuthorizationValidUntil > Now ? "authorized" : "pending_license",
                AuthorizationValidUntil = existing.AuthorizationValidUntil, LicenseObservationId = existing.LicenseObservationId };
        return await FinishContextAsync(claim, context, existing, proofDigest, cancellationToken,
            license.Status == SteamOwnershipStatus.ProviderUnavailable ? "PROVIDER_UNAVAILABLE" : null);
    }

    private GameContextRecord NewContext(AuthAttemptRecord attempt) => new()
    {
        Id = Guid.NewGuid(), ClientRunId = attempt.ClientRunId, ProtocolVersion = attempt.ProtocolVersion,
        Environment = options.Value.Environment, State = "pending_identity", CreatedAt = Now, AbsoluteExpiresAt = Now.AddHours(12),
        CredentialDigest = "", RefreshDigest = "",
    };

    private sealed record Claim(string Key, string Raw, AuthAttemptRecord Record);

    private async Task<(Claim? Claim, GameAuthReply? Prior)> ClaimAsync(string credential, string binding, Guid requestId,
        string? handoff, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(credential) || requestId == Guid.Empty) return (null, null);
        var key = attempts.Key(credential);
        for (var retry = 0; retry < 3; retry++)
        {
            var raw = await store.ReadAsync(key, cancellationToken);
            var record = GameAuthJson.Deserialize<AuthAttemptRecord>(raw);
            if (record is null || (record.Binding is not null && record.Binding != binding)) return (null, null);
            var receipt = attempts.Receipt(record, key, binding);
            if (receipt is not null)
            {
                if (receipt.GameContextCredential is not null && await GetContextAsync(receipt.GameContextCredential, false, cancellationToken) is null)
                    return (null, GameAuthReply.Failure("CONTEXT_REVOKED"));
                return (null, receipt);
            }
            if (record.Receipt is not null || record.ExpiresAt <= Now || record.Claims >= 3) return (null, null);
            if (record.WorkerUntil > Now) return (null, GameAuthReply.Failure("IN_PROGRESS"));
            if (handoff is not null && (record.Channel != "avalon" || record.ContextId is not null))
                return (null, GameAuthReply.Failure("INVALID_HANDOFF"));
            var claimed = record with { Binding = binding, WorkerId = Guid.NewGuid(), WorkerUntil = Now.AddSeconds(15), Claims = record.Claims + 1 };
            var changes = new List<GameAuthMutation>();
            if (handoff is not null && record.HandoffGrant is null)
            {
                var ticketKey = RedisGameTicketStore.Key(handoff);
                var value = await store.ReadAsync(ticketKey, cancellationToken);
                if (value is null || !RedisGameTicketStore.TryParseValue(value, true, out _)) return (null, GameAuthReply.Failure("INVALID_HANDOFF"));
                claimed = claimed with { HandoffGrant = value };
                changes.Add(new(ticketKey, value, null, record.ExpiresAt));
            }
            var next = GameAuthJson.Serialize(claimed);
            changes.Add(new(key, raw, next, record.ExpiresAt));
            if (await store.CompareExchangeAsync(changes, cancellationToken)) return (new(key, next, claimed), null);
        }
        return (null, GameAuthReply.Failure("IN_PROGRESS"));
    }

    private GameAuthMutation FinishMutation(Claim claim, GameAuthReply reply)
    {
        var expires = claim.Record.CreatedAt.AddMinutes(5);
        if (reply.ContextExpiresAt is { } credentialEnd && credentialEnd < expires) expires = credentialEnd;
        if (reply.State == "authorized" && reply.AuthorizationValidUntil is { } licenseEnd && licenseEnd < expires) expires = licenseEnd;
        return new(claim.Key, claim.Raw, GameAuthJson.Serialize(claim.Record with
        {
            Receipt = crypto.Protect(reply, attempts.ReceiptBinding(claim.Key, claim.Record.Binding!)), ReceiptExpiresAt = expires,
            WorkerId = null, WorkerUntil = null,
        }), claim.Record.CreatedAt.AddHours(12));
    }

    private GameAuthMutation ProofMutation(Claim claim, string digest) =>
        new(Key("proof", digest), null, claim.Record.Id.ToString("N"), claim.Record.CreatedAt.AddHours(12));

    private async Task<GameAuthReply> FinishErrorAsync(Claim claim, string error, CancellationToken cancellationToken, string? proofDigest = null)
    {
        var reply = GameAuthReply.Failure(error);
        var changes = new List<GameAuthMutation> { FinishMutation(claim, reply) };
        if (proofDigest is not null) changes.Add(ProofMutation(claim, proofDigest));
        return await store.CompareExchangeAsync(changes, cancellationToken) ? reply : GameAuthReply.Failure("INVALID_ATTEMPT");
    }

    private async Task<GameAuthReply> FinishContextAsync(Claim claim, GameContextRecord context, GameContextRecord? previous,
        string? proofDigest, CancellationToken cancellationToken, string? error = null)
    {
        var credential = GameAuthCryptography.NewToken();
        var refresh = GameAuthCryptography.NewToken();
        context = context with
        {
            CredentialDigest = GameAuthCryptography.Digest(credential), RefreshDigest = GameAuthCryptography.Digest(refresh),
            CredentialExpiresAt = Earlier(Now.AddMinutes(5), context.AbsoluteExpiresAt), Generation = (previous?.Generation ?? 0) + 1,
        };
        var contextKey = ContextKey(context.Id);
        var current = await store.ReadAsync(contextKey, cancellationToken);
        if ((previous is null && current is not null) || (previous is not null && current != GameAuthJson.Serialize(previous)))
            return GameAuthReply.Failure("CONTEXT_CHANGED");
        var reply = Response(context, credential, refresh, error);
        var changes = new List<GameAuthMutation>
        {
            FinishMutation(claim, reply), new(contextKey, current, GameAuthJson.Serialize(context), context.AbsoluteExpiresAt),
            new(TokenKey(credential), null, GameAuthJson.Serialize(new GameAuthTokenRecord(context.Id, "credential", context.Generation)), context.CredentialExpiresAt),
            new(TokenKey(refresh), null, GameAuthJson.Serialize(new GameAuthTokenRecord(context.Id, "refresh", context.Generation)), context.AbsoluteExpiresAt),
        };
        if (proofDigest is not null) changes.Add(ProofMutation(claim, proofDigest));
        if (context.PendingLinkId is { } pending)
            changes.Add(new(Key("pending-link", pending.ToString("N")), null, context.Id.ToString("N"), context.LinkProofExpiresAt!.Value));
        if (previous is not null)
        {
            var oldKey = Key("token", previous.RefreshDigest);
            var old = await store.ReadAsync(oldKey, cancellationToken);
            if (old is null) return GameAuthReply.Failure("CONTEXT_CHANGED");
            var retired = GameAuthJson.Deserialize<GameAuthTokenRecord>(old)! with { Spent = true };
            changes.Add(new(oldKey, old, GameAuthJson.Serialize(retired), context.AbsoluteExpiresAt));
        }
        return await store.CompareExchangeAsync(changes, cancellationToken) ? reply : GameAuthReply.Failure("CONTEXT_CHANGED");
    }

    private GameAuthReply Response(GameContextRecord context, string credential, string? refresh, string? error = null) => new()
    {
        State = context.State == "authorized" && !HasLicense(context) ? "pending_license" : context.State, Error = error,
        AccountId = context.AccountId?.ToString(CultureInfo.InvariantCulture), GameContextCredential = credential,
        GameContextRefreshToken = refresh, ContextExpiresAt = context.CredentialExpiresAt,
        AuthorizationValidUntil = context.AuthorizationValidUntil, LicenseSource = context.AuthorizationValidUntil is not null ? context.Provider : null,
        PendingLinkId = context.PendingLinkId?.ToString("N"), NextAction = context.State switch
        {
            "pending_link" => "link_account", "authorized" when HasLicense(context) => "select_world",
            _ when error == "PROVIDER_UNAVAILABLE" => "retry_license_check",
            _ when context.Provider is null => "play_through_steam", _ => "purchase_license",
        },
    };

    public async Task<GameAuthReply> RefreshAsync(string refresh, Guid requestId, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(refresh) || requestId == Guid.Empty) return GameAuthReply.Failure("INVALID_REFRESH");
        var tokenKey = TokenKey(refresh);
        for (var retry = 0; retry < 3; retry++)
        {
            var rawToken = await store.ReadAsync(tokenKey, cancellationToken);
            var token = GameAuthJson.Deserialize<GameAuthTokenRecord>(rawToken);
            if (token is null || token.Kind != "refresh") return GameAuthReply.Failure("INVALID_REFRESH");
            var contextKey = ContextKey(token.ContextId);
            var rawContext = await store.ReadAsync(contextKey, cancellationToken);
            var context = GameAuthJson.Deserialize<GameContextRecord>(rawContext);
            if (context is null || !await IsCurrentAsync(context, cancellationToken)) return GameAuthReply.Failure("CONTEXT_REVOKED");
            if (token.Spent)
            {
                if (token.RequestId == requestId && token.Receipt is not null && token.ReceiptExpiresAt > Now)
                    return crypto.Unprotect(token.Receipt, tokenKey + ":" + requestId.ToString("N"));
                if (!await store.CompareExchangeAsync([new(contextKey, rawContext,
                    GameAuthJson.Serialize(context with { State = "revoked" }), context.AbsoluteExpiresAt)], cancellationToken)) continue;
                return GameAuthReply.Failure("REFRESH_REUSE");
            }
            if (context.Generation != token.Generation || context.RefreshDigest != GameAuthCryptography.Digest(refresh))
                continue; // Rotation may have committed between the two reads. Re-read the token's exact retry receipt.
            var nextCredential = GameAuthCryptography.NewToken();
            var nextRefresh = GameAuthCryptography.NewToken();
            var next = context with
            {
                Generation = context.Generation + 1, CredentialDigest = GameAuthCryptography.Digest(nextCredential),
                RefreshDigest = GameAuthCryptography.Digest(nextRefresh), CredentialExpiresAt = Earlier(Now.AddMinutes(5), context.AbsoluteExpiresAt),
                State = context.State == "authorized" && !HasLicense(context) ? "pending_license" : context.State,
            };
            var reply = Response(next, nextCredential, nextRefresh);
            var spent = token with { Spent = true, RequestId = requestId,
                Receipt = crypto.Protect(reply, tokenKey + ":" + requestId.ToString("N")), ReceiptExpiresAt = Earlier(Now.AddSeconds(30), context.AbsoluteExpiresAt) };
            var changes = new GameAuthMutation[]
            {
                new(tokenKey, rawToken, GameAuthJson.Serialize(spent), context.AbsoluteExpiresAt),
                new(contextKey, rawContext, GameAuthJson.Serialize(next), context.AbsoluteExpiresAt),
                new(TokenKey(nextCredential), null, GameAuthJson.Serialize(new GameAuthTokenRecord(context.Id, "credential", next.Generation)), next.CredentialExpiresAt),
                new(TokenKey(nextRefresh), null, GameAuthJson.Serialize(new GameAuthTokenRecord(context.Id, "refresh", next.Generation)), next.AbsoluteExpiresAt),
            };
            if (await store.CompareExchangeAsync(changes, cancellationToken)) return reply;
        }
        return GameAuthReply.Failure("IN_PROGRESS");
    }

    public async Task<bool> LogoutAsync(string credential, CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(credential, false, cancellationToken);
        if (context is null) return false;
        return await store.CompareExchangeAsync([new(ContextKey(context.Id), GameAuthJson.Serialize(context),
            GameAuthJson.Serialize(context with { State = "revoked" }), context.AbsoluteExpiresAt)], cancellationToken);
    }

    private static DateTime Earlier(DateTime a, DateTime b) => a < b ? a : b;
}
