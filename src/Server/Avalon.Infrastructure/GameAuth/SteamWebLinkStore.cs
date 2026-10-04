using System.Globalization;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;

namespace Avalon.Infrastructure.GameAuth;

public sealed record SteamWebLinkStart(Guid Id, string Cookie);
public sealed record SteamWebLinkRecord(Guid Id, AccountId AccountId, string BrowserDigest, string CookieDigest,
    string CookieEnvelope, int CredentialsVersion, long SessionEpoch, DateTime ExpiresAt, string State = "created",
    string? SteamSubject = null, DateTime? ProofExpiresAt = null, Guid? ConfirmationId = null, Guid? ConfirmedMfaId = null,
    bool ConsolidationConsent = false);

/// <summary>Provider proofs belong to one browser transaction. Durable SQL intents take over after consent.</summary>
public sealed class SteamWebLinkStore(IGameContextStore store, GameAuthCryptography crypto, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Key(Guid id) => "steam-web:transaction:" + id.ToString("N");
    private bool Eligible(Account root) => root.Status == AccountStatus.Active && !root.IsLockedAt(Now) && root.GameplayConsolidationId is null &&
        (root.AccessLevel & Avalon.Common.Accounts.AccountAccessLevel.Player) != 0;
    private bool Authority(SteamWebLinkRecord record, Account root) => Eligible(root) && root.Id == record.AccountId &&
        root.CredentialsVersion == record.CredentialsVersion && root.SessionEpoch == record.SessionEpoch;
    private static bool Cookie(SteamWebLinkRecord record, string cookie) => GameAuthCryptography.IsToken(cookie) && record.CookieDigest == GameAuthCryptography.Digest(cookie);

    public async Task<SteamWebLinkStart?> StartAsync(Guid id, Account root, string browserSession, CancellationToken ct)
    {
        if (id == Guid.Empty || string.IsNullOrEmpty(browserSession) || browserSession.Length > 128 || !Eligible(root)) return null;
        var key = Key(id);
        for (var retry = 0; retry < 8; retry++)
        {
            var raw = await store.ReadAsync(key, ct);
            var existing = GameAuthJson.Deserialize<SteamWebLinkRecord>(raw);
            if (existing is not null)
                return existing.State == "created" && existing.ExpiresAt > Now && Authority(existing, root) && existing.BrowserDigest == GameAuthCryptography.Digest(browserSession)
                    ? new(id, crypto.UnprotectText(existing.CookieEnvelope, key)) : null;
            var cookie = GameAuthCryptography.NewToken();
            var record = new SteamWebLinkRecord(id, root.Id, GameAuthCryptography.Digest(browserSession), GameAuthCryptography.Digest(cookie),
                crypto.ProtectText(cookie, key), root.CredentialsVersion, root.SessionEpoch, Now.AddMinutes(5));
            if (await store.CompareExchangeAsync([new(key, null, GameAuthJson.Serialize(record), record.ExpiresAt)], ct)) return new(id, cookie);
        }
        return null;
    }
    public async Task<SteamWebLinkRecord?> ReadBoundAsync(Guid id, AccountId root, string browser, string cookie, CancellationToken ct)
    {
        var record = GameAuthJson.Deserialize<SteamWebLinkRecord>(await store.ReadAsync(Key(id), ct));
        return record is not null && record.AccountId == root && record.ExpiresAt > Now &&
            record.BrowserDigest == GameAuthCryptography.Digest(browser) && Cookie(record, cookie) ? record : null;
    }
    public async Task<SteamWebLinkRecord?> ReadCallbackAsync(Guid id, string cookie, CancellationToken ct)
    {
        var record = GameAuthJson.Deserialize<SteamWebLinkRecord>(await store.ReadAsync(Key(id), ct));
        return record is not null && record.ExpiresAt > Now && Cookie(record, cookie) ? record : null;
    }
    public async Task<bool> ChallengeAsync(Guid id, string cookie, CancellationToken ct)
    {
        var key = Key(id); var raw = await store.ReadAsync(key, ct);
        var record = GameAuthJson.Deserialize<SteamWebLinkRecord>(raw);
        return record is not null && record.State == "created" && record.ExpiresAt > Now && Cookie(record, cookie) &&
            await store.CompareExchangeAsync([new(key, raw, GameAuthJson.Serialize(record with { State = "challenged" }), record.ExpiresAt)], ct);
    }
    public async Task<bool> VerifyAsync(Guid id, string cookie, Account currentRoot, string claimedIdentity, string nonce, CancellationToken ct)
    {
        var subject = SteamSubject(claimedIdentity);
        if (subject is null || !FreshNonce(nonce, Now)) return false;
        var key = Key(id); var raw = await store.ReadAsync(key, ct);
        var record = GameAuthJson.Deserialize<SteamWebLinkRecord>(raw);
        if (record is null || record.State != "challenged" || record.ExpiresAt <= Now || !Cookie(record, cookie) || !Authority(record, currentRoot)) return false;
        var deadline = new[] { record.ExpiresAt, Now.AddMinutes(2) }.Min();
        return await store.CompareExchangeAsync([
            new(key, raw, GameAuthJson.Serialize(record with { State = "verified", SteamSubject = subject, ProofExpiresAt = deadline }), record.ExpiresAt),
            new("steam-web:nonce:" + GameAuthCryptography.Digest(nonce), null, "used", Now.AddMinutes(5))], ct);
    }
    public async Task<SteamWebLinkRecord?> CommitAsync(Guid id, Account root, string browser, string cookie, Guid confirm, Guid? mfaId, CancellationToken ct, bool consolidationConsent = false)
    {
        if (confirm == Guid.Empty) return null;
        var key = Key(id);
        for (var retry = 0; retry < 8; retry++)
        {
            var raw = await store.ReadAsync(key, ct);
            var record = GameAuthJson.Deserialize<SteamWebLinkRecord>(raw);
            if (record is null || record.ExpiresAt <= Now || record.AccountId != root.Id ||
                record.BrowserDigest != GameAuthCryptography.Digest(browser) || !Cookie(record, cookie)) return null;
            if (record.State == "committing") return record.ConfirmationId == confirm && record.ConsolidationConsent == consolidationConsent ? record : null;
            if (record.State != "verified" || record.ProofExpiresAt <= Now || !Authority(record, root)) return null;
            var next = record with { State = "committing", ConfirmationId = confirm, ConfirmedMfaId = mfaId,
                ConsolidationConsent = consolidationConsent, ExpiresAt = Now.AddHours(1) };
            if (await store.CompareExchangeAsync([new(key, raw, GameAuthJson.Serialize(next), next.ExpiresAt)], ct)) return next;
        }
        return null;
    }
    public static string? SteamSubject(string identity)
    {
        const string prefix = "https://steamcommunity.com/openid/id/";
        if (!identity.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var subject = identity[prefix.Length..];
        return subject.Length == 17 && subject[0] != '0' && subject.All(char.IsAsciiDigit) &&
            ulong.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? subject : null;
    }
    public static bool FreshNonce(string nonce, DateTime now) => nonce is { Length: > 20 and <= 256 } &&
        nonce.All(c => char.IsAscii(c) && !char.IsControl(c)) && DateTime.TryParseExact(nonce[..20], "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var issued) && issued > now.AddMinutes(-2) && issued <= now.AddSeconds(30);
}
