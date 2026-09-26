using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Exceptions;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;

namespace Avalon.Api.Services;

public interface IRefreshTokenService
{
    /// <summary>
    /// Opens a refresh-token family for a login that proved the credentials at
    /// <paramref name="credentialsVersion"/>. Refused with <see cref="AuthenticationException"/>
    /// (401) when the account's version has moved since (#495).
    /// </summary>
    Task<RefreshIssueResult> IssueAsync(AccountId accountId, int credentialsVersion,
        CancellationToken cancellationToken = default);
    Task<RefreshRotateResult> RotateAsync(string rawToken, CancellationToken cancellationToken = default);
    Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default);
    Task<int> RevokeAllForAccountAsync(AccountId accountId, CancellationToken cancellationToken = default);
}

public sealed record RefreshIssueResult(string RawToken, DateTime ExpiresAt, Guid FamilyId);
/// <param name="CredentialsVersion">The version the rotation held: the access token minted with it must carry it.</param>
public sealed record RefreshRotateResult(string RawToken, DateTime ExpiresAt, AccountId AccountId, int CredentialsVersion);

public sealed class RefreshTokenService : IRefreshTokenService
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);
    public const string CredentialsChanged = "Credentials changed; sign in again";

    /// <summary>
    /// How long after a rotation its parent may be presented again without being taken for a
    /// reuse, while the child it produced is still unused (#495 review): a second tab refreshing
    /// at the same moment, or a client retrying a refresh whose answer it lost. It gets 401 and
    /// nothing more; the family, and the session that won, survive.
    /// </summary>
    public static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(5);

    private readonly IRefreshTokenRepository _repository;
    private readonly ISecureRandom _random;
    private readonly TimeProvider _time;

    public RefreshTokenService(IRefreshTokenRepository repository, ISecureRandom random, TimeProvider time)
    {
        _repository = repository;
        _random = random;
        _time = time;
    }

    public async Task<RefreshIssueResult> IssueAsync(AccountId accountId, int credentialsVersion,
        CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var (raw, hash) = Generate();
        // Not a secret (tokens are looked up by hash); time-ordered because (AccountId, FamilyId) is indexed.
        var familyId = Guid.CreateVersion7();

        bool issued = await _repository.CreateIfCredentialsCurrentAsync(new RefreshToken
        {
            AccountId = accountId,
            FamilyId = familyId,
            Index = 0,
            Hash = hash,
            Revoked = false,
            Usages = 0,
            CreatedAt = now,
            ExpiresAt = now + DefaultLifetime,
            CredentialsVersion = credentialsVersion,
        }, cancellationToken);
        if (!issued)
            throw new AuthenticationException(CredentialsChanged);

        return new RefreshIssueResult(raw, now + DefaultLifetime, familyId);
    }

    public async Task<RefreshRotateResult> RotateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var row = await _repository.FindByHashAsync(hash, cancellationToken)
            ?? throw new UnauthorizedAccessException("Unknown refresh token");

        var now = _time.GetUtcNow().UtcDateTime;
        if (row.ExpiresAt <= now) throw new UnauthorizedAccessException("Refresh token expired");

        if (row.Revoked)
            await RefuseRevokedParentAsync(row, now, cancellationToken);

        var (newRaw, newHash) = Generate();
        var child = new RefreshToken
        {
            AccountId = row.AccountId,
            FamilyId = row.FamilyId,
            Index = row.Index + 1,
            Hash = newHash,
            Revoked = false,
            Usages = 0,
            CreatedAt = now,
            ExpiresAt = row.ExpiresAt,
            CredentialsVersion = row.CredentialsVersion,
        };

        switch (await _repository.RotateAsync(row, child, now, cancellationToken))
        {
            case RefreshRotation.Rotated:
                return new RefreshRotateResult(newRaw, row.ExpiresAt, row.AccountId, row.CredentialsVersion);
            case RefreshRotation.CredentialsChanged:
                // The family was opened before a password change or an MFA reset (#495). That
                // change revoked the token already; this refuses a rotation that read it just before.
                throw new UnauthorizedAccessException("Refresh token predates a credentials change");
            default:
                // Revoked between the read above and the write: another rotation of this token won
                // (#495), or it was revoked. Answered exactly as if it had arrived after that.
                await RefuseRevokedParentAsync(row, now, cancellationToken);
                throw new UnauthorizedAccessException("Refresh token revoked");
        }
    }

    /// <summary>
    /// Refuses a token that is no longer live. Inside <see cref="RotationGrace"/> of the rotation
    /// that replaced it, with that rotation's child still unused, it is a plain 401: two tabs or a
    /// retry, not a thief. Otherwise it is a reuse: the family is revoked and the caller is told
    /// (<see cref="RefreshTheftException"/>), which also ends the account's world sessions.
    /// </summary>
    private async Task RefuseRevokedParentAsync(RefreshToken row, DateTime now, CancellationToken cancellationToken)
    {
        RefreshToken? child = await _repository.FindChildAsync(row.FamilyId, row.Index, cancellationToken);
        if (child is { Revoked: false, Usages: 0 } && now - child.CreatedAt < RotationGrace)
            throw new RefreshAlreadyRotatedException();

        await _repository.RevokeFamilyAsync(row.FamilyId, cancellationToken);
        throw new RefreshTheftException(row.AccountId);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var row = await _repository.FindByHashAsync(hash, cancellationToken);
        if (row is null || row.Revoked) return;

        row.Revoked = true;
        await _repository.UpdateAsync(row, cancellationToken);
    }

    public Task<int> RevokeAllForAccountAsync(AccountId accountId, CancellationToken cancellationToken = default) =>
        _repository.RevokeAllForAccountAsync(accountId, cancellationToken);

    private (string RawToken, byte[] Hash) Generate()
    {
        var bytes = _random.GetBytes(32);
        var raw = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return (raw, hash);
    }
}
