using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Exceptions;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;

namespace Avalon.Api.Services;

public interface IPersonalAccessTokenService
{
    Task<MintResult> MintSelfAsync(AccountId callerId, AccountAccessLevel callerRoles, string name,
        DateTime? expiresAt, AccountAccessLevel? requestedRoles, CancellationToken cancellationToken = default);

    Task<MintResult> MintAdminAsync(AccountAccessLevel callerRoles, AccountId targetAccountId, string name,
        DateTime? expiresAt, AccountAccessLevel requestedRoles, CancellationToken cancellationToken = default);

    Task<PersonalAccessToken?> GetAsync(PersonalAccessTokenId id, CancellationToken cancellationToken = default);
    Task<List<PersonalAccessToken>> ListByAccountAsync(AccountId accountId, bool includeRevoked, CancellationToken cancellationToken = default);
    Task<PersonalAccessToken?> FindByRawTokenAsync(string token, CancellationToken cancellationToken = default);
    Task RevokeAsync(PersonalAccessToken token, AccountId revokedBy, CancellationToken cancellationToken = default);
    Task<int> RevokeAllForAccountAsync(AccountId accountId, AccountId revokedBy, CancellationToken cancellationToken = default);
    Task TouchLastUsedAsync(PersonalAccessTokenId id, CancellationToken cancellationToken = default);
}

public sealed record MintResult(
    PersonalAccessTokenId Id,
    string Name,
    string Token,
    string Prefix,
    DateTime? ExpiresAt,
    AccountAccessLevel Roles);

public class PersonalAccessTokenService : IPersonalAccessTokenService
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(365);
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(365);
    public const string TokenPrefix = "avp_";
    public const int TokenPrefixDisplayLength = 8;
    private static readonly TimeSpan LastUsedBucket = TimeSpan.FromSeconds(60);

    private readonly IPersonalAccessTokenRepository _repository;
    private readonly ISecureRandom _random;
    private readonly TimeProvider _time;

    public PersonalAccessTokenService(
        IPersonalAccessTokenRepository repository,
        ISecureRandom random,
        TimeProvider time)
    {
        _repository = repository;
        _random = random;
        _time = time;
    }

    public Task<MintResult> MintSelfAsync(AccountId callerId, AccountAccessLevel callerRoles, string name,
        DateTime? expiresAt, AccountAccessLevel? requestedRoles, CancellationToken cancellationToken = default)
    {
        var roles = requestedRoles ?? callerRoles;
        return MintInternalAsync(callerId, callerRoles, roles, name, expiresAt, cancellationToken);
    }

    public Task<MintResult> MintAdminAsync(AccountAccessLevel callerRoles, AccountId targetAccountId, string name,
        DateTime? expiresAt, AccountAccessLevel requestedRoles, CancellationToken cancellationToken = default)
    {
        return MintInternalAsync(targetAccountId, callerRoles, requestedRoles, name, expiresAt, cancellationToken);
    }

    private async Task<MintResult> MintInternalAsync(
        AccountId accountId,
        AccountAccessLevel callerRoles,
        AccountAccessLevel roles,
        string name,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
    {
        if ((roles & ~callerRoles) != 0)
        {
            throw new BusinessException("Requested roles exceed caller roles");
        }

        var now = _time.GetUtcNow().UtcDateTime;

        DateTime? resolvedExpiry;
        if (expiresAt is null)
        {
            resolvedExpiry = now + DefaultLifetime;
        }
        else
        {
            if (expiresAt.Value > now + MaxLifetime)
            {
                throw new BusinessException("PAT lifetime exceeds maximum of 365 days");
            }
            resolvedExpiry = expiresAt;
        }

        var rawBytes = _random.GetBytes(32);
        var base64Url = Convert.ToBase64String(rawBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        var token = TokenPrefix + base64Url;

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var prefix = token[..TokenPrefixDisplayLength];

        var entity = new PersonalAccessToken
        {
            AccountId = accountId,
            Name = name,
            TokenHash = tokenHash,
            TokenPrefix = prefix,
            Roles = roles,
            CreatedAt = now,
            ExpiresAt = resolvedExpiry,
        };

        var created = await _repository.CreateAsync(entity, cancellationToken);

        return new MintResult(
            created.Id,
            created.Name,
            token,
            prefix,
            created.ExpiresAt,
            created.Roles);
    }

    public Task<PersonalAccessToken?> GetAsync(PersonalAccessTokenId id, CancellationToken cancellationToken = default) =>
        _repository.FindByIdAsync(id, track: false, cancellationToken);

    public Task<List<PersonalAccessToken>> ListByAccountAsync(AccountId accountId, bool includeRevoked, CancellationToken cancellationToken = default) =>
        _repository.ListByAccountAsync(accountId, includeRevoked, cancellationToken);

    public Task<PersonalAccessToken?> FindByRawTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return _repository.FindByHashAsync(hash, cancellationToken);
    }

    public Task RevokeAsync(PersonalAccessToken token, AccountId revokedBy, CancellationToken cancellationToken = default)
    {
        token.RevokedAt = _time.GetUtcNow().UtcDateTime;
        token.RevokedBy = revokedBy;
        return _repository.UpdateAsync(token, cancellationToken);
    }

    public Task<int> RevokeAllForAccountAsync(AccountId accountId, AccountId revokedBy, CancellationToken cancellationToken = default) =>
        _repository.RevokeAllForAccountAsync(accountId, revokedBy, cancellationToken);

    public Task TouchLastUsedAsync(PersonalAccessTokenId id, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return _repository.UpdateLastUsedIfStaleAsync(id, now, LastUsedBucket, cancellationToken);
    }
}
