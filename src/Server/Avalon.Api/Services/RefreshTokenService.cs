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
    Task<RefreshIssueResult> IssueAsync(AccountId accountId, CancellationToken cancellationToken = default);
    Task<RefreshRotateResult> RotateAsync(string rawToken, CancellationToken cancellationToken = default);
    Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default);
    Task<int> RevokeAllForAccountAsync(AccountId accountId, CancellationToken cancellationToken = default);
}

public sealed record RefreshIssueResult(string RawToken, DateTime ExpiresAt, Guid FamilyId);
public sealed record RefreshRotateResult(string RawToken, DateTime ExpiresAt, AccountId AccountId);

public sealed class RefreshTokenService : IRefreshTokenService
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

    private readonly IRefreshTokenRepository _repository;
    private readonly ISecureRandom _random;
    private readonly TimeProvider _time;

    public RefreshTokenService(IRefreshTokenRepository repository, ISecureRandom random, TimeProvider time)
    {
        _repository = repository;
        _random = random;
        _time = time;
    }

    public async Task<RefreshIssueResult> IssueAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var (raw, hash) = Generate();
        // Not a secret (tokens are looked up by hash); time-ordered because (AccountId, FamilyId) is indexed.
        var familyId = Guid.CreateVersion7();

        await _repository.CreateAsync(new RefreshToken
        {
            AccountId = accountId,
            FamilyId = familyId,
            Index = 0,
            Hash = hash,
            Revoked = false,
            Usages = 0,
            CreatedAt = now,
            ExpiresAt = now + DefaultLifetime,
        }, cancellationToken);

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
        {
            await _repository.RevokeFamilyAsync(row.FamilyId, cancellationToken);
            throw new RefreshTheftException(row.AccountId);
        }

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
        };

        switch (await _repository.RotateAsync(row, child, now, cancellationToken))
        {
            case RefreshRotation.Rotated:
                return new RefreshRotateResult(newRaw, row.ExpiresAt, row.AccountId);
            case RefreshRotation.CredentialsChanged:
                // Issued before a password change or an MFA reset (#495). That change revoked the
                // token already; this refuses a rotation that read it just before.
                throw new UnauthorizedAccessException("Refresh token predates a credentials change");
            default:
                throw new UnauthorizedAccessException("Refresh token revoked");
        }
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
