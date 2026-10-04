using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.GameAuth;

public sealed record LinkBrowserReply(string State, string? Error = null, string? SteamId = null,
    string? Username = null, DateTime? ExpiresAt = null, bool RequiresMfa = false)
{
    public override string ToString() => $"Browser store consent: {State}, error: {Error} (credential redacted)";
}

public sealed record LinkProposalReply(string State, string? Error = null, string? AccountId = null,
    string? Username = null, string? ConsentCode = null, DateTime? ExpiresAt = null)
{
    public override string ToString() => $"Account link proposal: {State} (consent code redacted)";
}


internal sealed record LinkConsentRecord
{
    public Guid OperationId { get; init; }
    public Guid BrowserRequestId { get; init; }
    public Guid PendingLinkId { get; init; }
    public Guid ContextId { get; init; }
    public int ContextGeneration { get; init; }
    public required string CredentialDigest { get; init; }
    public required string Challenge { get; init; }
    public required string ProviderSubject { get; init; }
    public long AccountId { get; init; }
    public int CredentialsVersion { get; init; }
    public long SessionEpoch { get; init; }
    public Guid? ConfirmedMfaId { get; init; }
    public required string Username { get; init; }
    public required string CodeDigest { get; init; }
    public required string CodeEnvelope { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime ProofExpiresAt { get; init; }
    public bool Canceled { get; init; }
    public string? Binding { get; init; }
    public DateTime? WorkerUntil { get; init; }
    public string? Receipt { get; init; }
    public DateTime? ReceiptExpiresAt { get; init; }
}

/// <summary>The browser grants consent; only the original game can retrieve and accept it.</summary>
public sealed class PendingLinkStore(GameAuthorizationService authorization, IGameContextStore store,
    GameAuthCryptography crypto, IAccountRepository accounts, IMfaSetupRepository mfa,
    IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    internal string ConsentKey(Guid id) => CacheKeys.GameAuth(options.Value.Environment, "link-consent", id.ToString("N"));
    internal string CodeKey(string digest) => CacheKeys.GameAuth(options.Value.Environment, "link-code", digest);
    private string PendingKey(Guid id) => CacheKeys.GameAuth(options.Value.Environment, "pending-link", id.ToString("N"));

    private async Task<GameContextRecord?> PendingAsync(Guid id, CancellationToken cancellationToken)
    {
        var value = await store.ReadAsync(PendingKey(id), cancellationToken);
        if (!Guid.TryParseExact(value, "N", out var contextId)) return null;
        var context = await authorization.GetContextByIdAsync(contextId, false, cancellationToken);
        return context is { State: "pending_link", Provider: "steam", ProviderSubject: not null } &&
               context.PendingLinkId == id && context.LinkProofExpiresAt > Now ? context : null;
    }

    public async Task<LinkBrowserReply> InfoAsync(Guid id, AccountId accountId, CancellationToken cancellationToken)
    {
        var context = await PendingAsync(id, cancellationToken);
        var account = await accounts.FindByIdAsync(accountId, false, cancellationToken);
        if (context is null || account is null || (context.AccountId is { } bound && bound != accountId.Value))
            return new("expired", "INVALID_LINK");
        var setup = await mfa.FindByAccountIdAsync(accountId, cancellationToken);
        return new("pending", SteamId: context.ProviderSubject, Username: account.Username,
            ExpiresAt: context.LinkProofExpiresAt, RequiresMfa: setup?.Status == MfaSetupStatus.Confirmed);
    }

    // The API invokes this only after current-password and, when enrolled, current TOTP verification.
    public async Task<LinkBrowserReply> ConfirmAsync(Guid id, AccountId accountId, int credentialsVersion,
        long sessionEpoch, Guid? confirmedMfaId, Guid requestId, CancellationToken cancellationToken)
    {
        var context = await PendingAsync(id, cancellationToken);
        var account = await accounts.FindByIdAsync(accountId, false, cancellationToken);
        if (requestId == Guid.Empty || context is null || account is null || account.Status != AccountStatus.Active ||
            account.IsLockedAt(Now) || (account.AccessLevel & AccountAccessLevel.Player) == 0 ||
            account.CredentialsVersion != credentialsVersion || account.SessionEpoch != sessionEpoch ||
            (context.AccountId is { } root && root != accountId.Value)) return new("pending", "ACCOUNT_UNAVAILABLE");
        var setup = await mfa.FindByAccountIdAsync(accountId, cancellationToken);
        if ((setup?.Status == MfaSetupStatus.Confirmed ? setup.Id : (Guid?)null) != confirmedMfaId)
            return new("pending", "MFA_REQUIRED");
        var key = ConsentKey(id);
        var existing = GameAuthJson.Deserialize<LinkConsentRecord>(await store.ReadAsync(key, cancellationToken));
        if (existing is not null)
            return existing.BrowserRequestId == requestId && existing.AccountId == accountId.Value && !existing.Canceled &&
                   existing.ExpiresAt > Now ? new("awaiting_game_confirmation", Username: existing.Username, ExpiresAt: existing.ExpiresAt) : new("pending", "LINK_ALREADY_PROPOSED");
        var code = GameAuthCryptography.NewToken();
        var expires = Now.AddSeconds(60) < context.LinkProofExpiresAt ? Now.AddSeconds(60) : context.LinkProofExpiresAt!.Value;
        var consent = new LinkConsentRecord
        {
            OperationId = Guid.NewGuid(), BrowserRequestId = requestId, PendingLinkId = id,
            ContextId = context.Id, ContextGeneration = context.Generation, CredentialDigest = context.CredentialDigest,
            Challenge = context.LinkChallenge!, ProviderSubject = context.ProviderSubject!, AccountId = accountId.Value,
            CredentialsVersion = credentialsVersion, SessionEpoch = sessionEpoch, ConfirmedMfaId = confirmedMfaId,
            Username = account.Username, CodeDigest = GameAuthCryptography.Digest(code),
            CodeEnvelope = crypto.ProtectText(code, key), ExpiresAt = expires, ProofExpiresAt = context.LinkProofExpiresAt!.Value,
        };
        var contextKey = CacheKeys.GameAuth(options.Value.Environment, "context", context.Id.ToString("N"));
        var mutations = new GameAuthMutation[]
        {
            new(contextKey, GameAuthJson.Serialize(context), GameAuthJson.Serialize(context), context.AbsoluteExpiresAt),
            new(key, null, GameAuthJson.Serialize(consent), consent.ProofExpiresAt),
            new(CodeKey(consent.CodeDigest), null, id.ToString("N"), consent.ProofExpiresAt),
        };
        return await store.CompareExchangeAsync(mutations, cancellationToken)
            ? new("awaiting_game_confirmation", Username: consent.Username, ExpiresAt: expires)
            : new("pending", "CONTEXT_CHANGED");
    }

    public async Task<LinkProposalReply> ProposalAsync(string credential, string verifier, CancellationToken cancellationToken)
    {
        var context = await authorization.GetContextAsync(credential, false, cancellationToken);
        if (context is not { State: "pending_link", PendingLinkId: not null } || !MatchesPkce(context.LinkChallenge, verifier))
            return new("pending", "INVALID_LINK");
        var key = ConsentKey(context.PendingLinkId.Value);
        var consent = GameAuthJson.Deserialize<LinkConsentRecord>(await store.ReadAsync(key, cancellationToken));
        if (consent is null) return new("pending");
        if (consent.Canceled || consent.ExpiresAt <= Now || consent.ContextGeneration != context.Generation ||
            consent.CredentialDigest != context.CredentialDigest || consent.Binding is not null) return new("pending", "INVALID_LINK");
        return new("awaiting_game_confirmation", AccountId: consent.AccountId.ToString(CultureInfo.InvariantCulture),
            Username: consent.Username, ConsentCode: crypto.UnprotectText(consent.CodeEnvelope, key), ExpiresAt: consent.ExpiresAt);
    }

    public async Task<bool> CancelAsync(Guid id, AccountId accountId, CancellationToken cancellationToken)
    {
        var key = ConsentKey(id);
        var raw = await store.ReadAsync(key, cancellationToken);
        var consent = GameAuthJson.Deserialize<LinkConsentRecord>(raw);
        return consent is not null && consent.AccountId == accountId.Value && consent.Binding is null && consent.ProofExpiresAt > Now &&
            await store.CompareExchangeAsync([new(key, raw, GameAuthJson.Serialize(consent with { Canceled = true }), consent.ProofExpiresAt)], cancellationToken);
    }

    internal static bool MatchesPkce(string? challenge, string? verifier)
    {
        if (challenge is not { Length: 43 } || verifier is not { Length: >= 43 and <= 128 } ||
            !verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~')) return false;
        var calculated = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge), Encoding.ASCII.GetBytes(calculated));
    }
}
