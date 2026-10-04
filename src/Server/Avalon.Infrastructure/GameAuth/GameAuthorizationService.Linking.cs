using Avalon.Common.GameAuth;
using System.Globalization;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.StoreAuth;

namespace Avalon.Infrastructure.GameAuth;

public sealed partial class GameAuthorizationService
{
    public async Task<GameAuthReply> CompleteAccountLinkAsync(PendingLinkStore links, string credential, string code,
        string pkceVerifier, Guid requestId, bool accepted, CancellationToken cancellationToken)
    {
        if (!accepted || requestId == Guid.Empty || !GameAuthCryptography.IsToken(credential) || !GameAuthCryptography.IsToken(code))
            return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
        var codeDigest = GameAuthCryptography.Digest(code);
        var pending = await store.ReadAsync(links.CodeKey(codeDigest), cancellationToken);
        if (!Guid.TryParseExact(pending, "N", out var pendingId)) return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
        var key = links.ConsentKey(pendingId);
        var binding = crypto.Binding("link", requestId,
            GameAuthCryptography.Digest(credential + ":" + code + ":" + pkceVerifier));
        for (var retry = 0; retry < GameAuthPolicy.MutationAttempts; retry++)
        {
            var raw = await store.ReadAsync(key, cancellationToken);
            var consent = GameAuthJson.Deserialize<LinkConsentRecord>(raw);
            if (consent is null || consent.Canceled || consent.ProofExpiresAt <= Now || consent.CodeDigest != codeDigest ||
                consent.CredentialDigest != GameAuthCryptography.Digest(credential) || !PendingLinkStore.MatchesPkce(consent.Challenge, pkceVerifier) ||
                (consent.Binding is not null && consent.Binding != binding)) return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
            if (consent.Receipt is not null)
            {
                if (consent.ReceiptExpiresAt <= Now) return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
                var receipt = crypto.Unprotect(consent.Receipt, key + ":" + binding);
                return receipt.GameContextCredential is null || await GetContextAsync(receipt.GameContextCredential, false, cancellationToken) is not null
                    ? receipt : GameAuthReply.Failure(GameAuthErrors.ContextRevoked);
            }
            if (consent.Binding is null && consent.ExpiresAt <= Now) return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
            if (consent.WorkerUntil > Now) return GameAuthReply.Failure(GameAuthErrors.InProgress);
            var token = GameAuthJson.Deserialize<GameAuthTokenRecord>(await store.ReadAsync(TokenKey(credential), cancellationToken));
            var contextKey = ContextKey(consent.ContextId);
            var rawContext = await store.ReadAsync(contextKey, cancellationToken);
            var context = GameAuthJson.Deserialize<GameContextRecord>(rawContext);
            if (token is null || token.Kind != GameAuthTokenKinds.Credential || token.Spent || token.ContextId != consent.ContextId ||
                token.Generation != consent.ContextGeneration || context is not { State: GameAuthStates.PendingLink } || context.Generation != token.Generation ||
                context.PendingLinkId != pendingId || context.CredentialDigest != consent.CredentialDigest || context.CredentialExpiresAt <= Now ||
                context.LinkProofExpiresAt <= Now || context.AbsoluteExpiresAt <= Now || context.Environment != options.Value.Environment ||
                context.Audience != GameAuthPolicy.ContextAudience || context.Product != StoreAuthenticationConfiguration.Product ||
                context.Provider != StoreProviders.Steam || context.ProviderSubject != consent.ProviderSubject ||
                (context.AccountId is { } accountId && accountId != consent.AccountId)) return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
            if (context.LauncherFamilyId is { } family &&
                !await refreshTokens.IsLiveLauncherFamilyAsync(new AccountId(consent.AccountId), family, Now, cancellationToken))
                return GameAuthReply.Failure(GameAuthErrors.ContextRevoked);
            // The durable repository validates current password/epoch/MFA authority, including an exact
            // retry of this operation after a DB commit. No general context epoch bypass is exposed.
            var claimed = consent with { Binding = binding, WorkerUntil = Earlier(Now.Add(GameAuthPolicy.MutationClaimLifetime), consent.ProofExpiresAt) };
            var claimedRaw = GameAuthJson.Serialize(claimed);
            if (!await store.CompareExchangeAsync([new(key, raw, claimedRaw, consent.ProofExpiresAt)], cancellationToken)) continue;
            var operation = new IdentityLinkOperation(consent.OperationId, new AccountId(consent.AccountId), StoreProviders.Steam,
                consent.ProviderSubject, consent.CredentialsVersion, consent.SessionEpoch, consent.ConfirmedMfaId)
                { ProofExpiresAt = consent.ProofExpiresAt };
            var result = await identities.LinkWithAuthorityAsync(operation, Now, cancellationToken);
            if (result.Status is not (IdentityLinkStatus.Linked or IdentityLinkStatus.AlreadyLinked) || result.Identity?.Id != operation.OperationId)
            {
                var failure = GameAuthReply.Failure(result.Status switch
                {
                    IdentityLinkStatus.SubjectTaken or IdentityLinkStatus.AccountProviderTaken => GameAuthErrors.AccountLinkConflict,
                    IdentityLinkStatus.UsernameTaken or IdentityLinkStatus.EmailTaken => GameAuthErrors.RegistrationDetailsTaken,
                    IdentityLinkStatus.CreationRefused => GameAuthErrors.RegistrationLimit,
                    _ => GameAuthErrors.AccountUnavailable,
                });
                await store.CompareExchangeAsync([new(key, claimedRaw, GameAuthJson.Serialize(claimed with
                { Receipt = crypto.Protect(failure, key + ":" + binding), ReceiptExpiresAt = consent.ProofExpiresAt, WorkerUntil = null }), consent.ProofExpiresAt)], cancellationToken);
                return failure;
            }
            var resolvedAccountId = operation.AccountId;
            var account = await accounts.FindByIdAsync(resolvedAccountId, false, cancellationToken);
            if (!Eligible(account, resolvedAccountId.Value) || account!.CredentialsVersion != consent.CredentialsVersion ||
                account.SessionEpoch != consent.SessionEpoch + 1) return GameAuthReply.Failure(GameAuthErrors.AccountUnavailable);
            var license = await ownership.CheckAsync(consent.ProviderSubject, cancellationToken);
            if (license.ProviderSubject != consent.ProviderSubject || license.AuthorizedUntil > license.ObservedAt.Add(GameAuthPolicy.OwnershipLifetime))
                return GameAuthReply.Failure(GameAuthErrors.ProviderUnavailable);
            Guid? observationId = null;
            if (license.Status != SteamOwnershipStatus.ProviderUnavailable)
            {
                observationId = Guid.NewGuid();
                await observations.RecordAsync(new LicenseObservation
                {
                    Id = observationId.Value, AccountId = account.Id, Provider = StoreProviders.Steam, ProviderSubject = consent.ProviderSubject,
                    ProviderOwnerSubject = license.OwnerSubject, Permanent = license.Permanent,
                    OwnsProduct = license.Status == SteamOwnershipStatus.Owned, Environment = options.Value.Environment,
                    Product = StoreAuthenticationConfiguration.Product, ProviderAppId = options.Value.SteamAppId.ToString(CultureInfo.InvariantCulture),
                    ObservedAt = license.ObservedAt, AuthorizedUntil = license.AuthorizedUntil, ProviderExpiresAt = license.ProviderExpiresAt,
                    PolicyVersion = options.Value.PolicyVersion,
                }, cancellationToken);
            }
            if (consent.ProofExpiresAt <= Now) return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
            var nextCredential = GameAuthCryptography.NewToken();
            var nextRefresh = GameAuthCryptography.NewToken();
            var next = context with
            {
                AccountId = account.Id.Value, CredentialsVersion = account.CredentialsVersion, SessionEpoch = account.SessionEpoch,
                State = license.Status == SteamOwnershipStatus.Owned && license.AuthorizedUntil > Now ? GameAuthStates.Authorized : GameAuthStates.PendingLicense,
                AuthorizationValidUntil = license.Status == SteamOwnershipStatus.Owned ? license.AuthorizedUntil : null,
                LicenseObservationId = observationId, PendingLinkId = null, LinkChallenge = null, LinkProofExpiresAt = null,
                CredentialDigest = GameAuthCryptography.Digest(nextCredential), RefreshDigest = GameAuthCryptography.Digest(nextRefresh),
                CredentialExpiresAt = Earlier(Now.Add(GameAuthPolicy.CredentialLifetime), context.AbsoluteExpiresAt), Generation = context.Generation + 1,
            };
            var reply = Response(next, nextCredential, nextRefresh,
                license.Status == SteamOwnershipStatus.ProviderUnavailable ? GameAuthErrors.ProviderUnavailable : null);
            var oldRefreshKey = Key("token", context.RefreshDigest);
            var oldRefreshRaw = await store.ReadAsync(oldRefreshKey, cancellationToken);
            var oldRefresh = GameAuthJson.Deserialize<GameAuthTokenRecord>(oldRefreshRaw);
            if (oldRefresh is null) return GameAuthReply.Failure(GameAuthErrors.ContextChanged);
            var receiptExpires = Earlier(consent.ProofExpiresAt, next.CredentialExpiresAt);
            if (next.AuthorizationValidUntil is { } deadline && deadline < receiptExpires) receiptExpires = deadline;
            var finished = claimed with { WorkerUntil = null, Receipt = crypto.Protect(reply, key + ":" + binding), ReceiptExpiresAt = receiptExpires };
            var changes = new GameAuthMutation[]
            {
                new(key, claimedRaw, GameAuthJson.Serialize(finished), consent.ProofExpiresAt),
                new(contextKey, rawContext, GameAuthJson.Serialize(next), context.AbsoluteExpiresAt),
                new(TokenKey(nextCredential), null, GameAuthJson.Serialize(new GameAuthTokenRecord(next.Id, GameAuthTokenKinds.Credential, next.Generation)), next.CredentialExpiresAt),
                new(TokenKey(nextRefresh), null, GameAuthJson.Serialize(new GameAuthTokenRecord(next.Id, GameAuthTokenKinds.Refresh, next.Generation)), next.AbsoluteExpiresAt),
                new(oldRefreshKey, oldRefreshRaw, GameAuthJson.Serialize(oldRefresh with { Spent = true }), context.AbsoluteExpiresAt),
            };
            return await store.CompareExchangeAsync(changes, cancellationToken) ? reply : GameAuthReply.Failure(GameAuthErrors.ContextChanged);
        }
        return GameAuthReply.Failure(GameAuthErrors.InProgress);
    }
}
