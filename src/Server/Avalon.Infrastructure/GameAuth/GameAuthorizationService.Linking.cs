using Avalon.Common.GameAuth;
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
        string codeDigest = GameAuthCryptography.Digest(code);
        string? pending = await store.ReadAsync(links.CodeKey(codeDigest), cancellationToken);
        if (!Guid.TryParseExact(pending, "N", out Guid pendingId)) return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
        string key = links.ConsentKey(pendingId);
        string binding = crypto.Binding("link", requestId,
            GameAuthCryptography.Digest(credential + ":" + code + ":" + pkceVerifier));
        for (int retry = 0; retry < GameAuthPolicy.MutationAttempts; retry++)
        {
            string? raw = await store.ReadAsync(key, cancellationToken);
            LinkConsentRecord? consent = GameAuthJson.Deserialize<LinkConsentRecord>(raw);
            if (consent is null || consent.Canceled || consent.ProofExpiresAt <= Now || consent.CodeDigest != codeDigest ||
                consent.CredentialDigest != GameAuthCryptography.Digest(credential) || !PendingLinkStore.MatchesPkce(consent.Challenge, pkceVerifier) ||
                (consent.Binding is not null && consent.Binding != binding))
            {
                return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
            }

            if (consent.Receipt is not null)
            {
                if (consent.ReceiptExpiresAt <= Now) return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
                GameAuthReply receipt = crypto.Unprotect(consent.Receipt, key + ":" + binding);
                return receipt.GameContextCredential is null || await GetContextAsync(receipt.GameContextCredential, false, cancellationToken) is not null
                    ? receipt : GameAuthReply.Failure(GameAuthErrors.ContextRevoked);
            }
            if (consent.Binding is null && consent.ExpiresAt <= Now) return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
            if (consent.WorkerUntil > Now) return GameAuthReply.Failure(GameAuthErrors.InProgress);
            GameAuthTokenRecord? token = GameAuthJson.Deserialize<GameAuthTokenRecord>(await store.ReadAsync(TokenKey(credential), cancellationToken));
            string contextKey = ContextKey(consent.ContextId);
            string? rawContext = await store.ReadAsync(contextKey, cancellationToken);
            GameContextRecord? context = GameAuthJson.Deserialize<GameContextRecord>(rawContext);
            if (token is null || token.Kind != GameAuthTokenKinds.Credential || token.Spent || token.ContextId != consent.ContextId ||
                token.Generation != consent.ContextGeneration || context is not { State: GameAuthStates.PendingLink } || context.Generation != token.Generation ||
                context.PendingLinkId != pendingId || context.CredentialDigest != consent.CredentialDigest || context.CredentialExpiresAt <= Now ||
                options.Value.ResolveApplication(context.ApplicationKey) is null || context.ApplicationKey != consent.ApplicationKey || context.LinkProofExpiresAt <= Now || context.AbsoluteExpiresAt <= Now || context.Environment != options.Value.Environment ||
                context.Audience != GameAuthPolicy.ContextAudience || context.Product != StoreAuthenticationConfiguration.Product ||
                context.Provider != consent.Provider || context.ProviderSubject != consent.ProviderSubject ||
                (context.AccountId is { } accountId && accountId != consent.AccountId))
            {
                return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
            }

            if (context.LauncherFamilyId is { } family &&
                !await refreshTokens.IsLiveLauncherFamilyAsync(new AccountId(consent.AccountId), family, Now, cancellationToken))
            {
                return GameAuthReply.Failure(GameAuthErrors.ContextRevoked);
            }
            // The durable repository validates current password/epoch/MFA authority, including an exact
            // retry of this operation after a DB commit. No general context epoch bypass is exposed.
            LinkConsentRecord claimed = consent with { Binding = binding, WorkerUntil = Earlier(Now.Add(GameAuthPolicy.MutationClaimLifetime), consent.ProofExpiresAt) };
            string claimedRaw = GameAuthJson.Serialize(claimed);
            if (!await store.CompareExchangeAsync([new(key, raw, claimedRaw, consent.ProofExpiresAt)], cancellationToken)) continue;
            var operation = new IdentityLinkOperation(consent.OperationId, new AccountId(consent.AccountId), consent.Provider,
                consent.ProviderSubject, consent.CredentialsVersion, consent.SessionEpoch, consent.ConfirmedMfaId)
            { ProofExpiresAt = consent.ProofExpiresAt };
            IdentityLinkResult result = await identities.LinkWithAuthorityAsync(operation, Now, cancellationToken);
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
            AccountId resolvedAccountId = operation.AccountId;
            Account? account = await accounts.FindByIdAsync(resolvedAccountId, false, cancellationToken);
            if (!Eligible(account, resolvedAccountId.Value) || account!.CredentialsVersion != consent.CredentialsVersion ||
                account.SessionEpoch != consent.SessionEpoch + 1)
            {
                return GameAuthReply.Failure(GameAuthErrors.AccountUnavailable);
            }

            GameApplicationSelection application = options.Value.ResolveApplication(context.ApplicationKey)!;
            GameLicenseAuthorityResult license = await licenseAuthority.VerifyAsync(new(account.Id, application,
                new(consent.ProviderSubject, context.IdentityVerifiedAt!.Value, context.IdentityValidUntil!.Value),
                null, null, Now), cancellationToken);
            if (license.Status == GameLicenseCheckStatus.Unavailable)
            {
                await store.CompareExchangeAsync([new(key, claimedRaw, GameAuthJson.Serialize(claimed with { WorkerUntil = Now }), consent.ProofExpiresAt)], cancellationToken);
                return GameAuthReply.Failure(GameAuthErrors.ProviderUnavailable);
            }
            if (consent.ProofExpiresAt <= Now) return GameAuthReply.Failure(GameAuthErrors.InvalidLink);
            string nextCredential = GameAuthCryptography.NewToken();
            string nextRefresh = GameAuthCryptography.NewToken();
            GameContextRecord next = context with
            {
                AccountId = account.Id.Value,
                CredentialsVersion = account.CredentialsVersion,
                SessionEpoch = account.SessionEpoch,
                State = license.Status == GameLicenseCheckStatus.Licensed && license.AuthorizedUntil > Now ? GameAuthStates.Authorized : GameAuthStates.PendingLicense,
                AuthorizationValidUntil = license.Status == GameLicenseCheckStatus.Licensed ? license.AuthorizedUntil : null,
                LicenseObservationId = license.ObservationId,
                LicenseId = license.LicenseId,
                LicenseRevision = license.Revision,
                PendingLinkId = null,
                LinkChallenge = null,
                LinkProofExpiresAt = null,
                CredentialDigest = GameAuthCryptography.Digest(nextCredential),
                RefreshDigest = GameAuthCryptography.Digest(nextRefresh),
                CredentialExpiresAt = Earlier(Now.Add(GameAuthPolicy.CredentialLifetime), context.AbsoluteExpiresAt),
                Generation = context.Generation + 1,
            };
            GameAuthReply reply = Response(next, nextCredential, nextRefresh,
                license.Status == GameLicenseCheckStatus.Unavailable ? GameAuthErrors.ProviderUnavailable : null);
            string oldRefreshKey = Key("token", context.RefreshDigest);
            string? oldRefreshRaw = await store.ReadAsync(oldRefreshKey, cancellationToken);
            GameAuthTokenRecord? oldRefresh = GameAuthJson.Deserialize<GameAuthTokenRecord>(oldRefreshRaw);
            if (oldRefresh is null) return GameAuthReply.Failure(GameAuthErrors.ContextChanged);
            DateTime receiptExpires = Earlier(consent.ProofExpiresAt, next.CredentialExpiresAt);
            if (next.AuthorizationValidUntil is { } deadline && deadline < receiptExpires) receiptExpires = deadline;
            LinkConsentRecord finished = claimed with { WorkerUntil = null, Receipt = crypto.Protect(reply, key + ":" + binding), ReceiptExpiresAt = receiptExpires };
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
