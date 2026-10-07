using System.Globalization;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;

namespace Avalon.World.GameAuth;

// The internal API's wire response. These values arrive only over workload-authenticated HTTPS.
public sealed record SessionLeaseResponse
{
    public string State { get; init; } = GameAuthStates.Pending;
    public string? Error { get; init; }
    public string? AccountId { get; init; }
    public string? GameSessionId { get; init; }
    public string? GameContextId { get; init; }
    public string? FencingToken { get; init; }
    public string? ServerId { get; init; }
    public ushort? WorldId { get; init; }
    public ushort? AccessLevel { get; init; }
    public int? CredentialsVersion { get; init; }
    public string? SessionEpoch { get; init; }
    public DateTime? LeaseUntil { get; init; }
    public DateTime? AuthorizationUntil { get; init; }
}

/// <summary>One writer for the connection's lifetime. Expiry and revocation cannot be undone by a late reply.</summary>
public sealed class GameSessionLease
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly SessionLeaseResponse _identity;
    private DateTime _until;
    private long _started;
    private TimeSpan _duration;
    private bool _revoked;

    private GameSessionLease(SessionLeaseResponse response, GameplayWriteAuthority authority, TimeProvider clock)
    {
        _identity = response;
        _clock = clock;
        Authority = authority;
        SetDeadline(response.LeaseUntil!.Value);
    }

    public GameplayWriteAuthority Authority { get; }
    public Guid GameContextId => Guid.ParseExact(_identity.GameContextId!, "D");
    public AccountAccessLevel AccessLevel => (AccountAccessLevel)_identity.AccessLevel!.Value;
    public string ServerId => _identity.ServerId!;
    public ushort WorldId => _identity.WorldId!.Value;
    public bool IsActive { get { lock (_gate) return ActiveUnderLock(); } }
    public DateTime LeaseUntil { get { lock (_gate) return _until; } }
    public TimeSpan Remaining
    {
        get
        {
            lock (_gate)
            {
                if (!ActiveUnderLock()) return TimeSpan.Zero;
                return TimeSpan.FromTicks(Math.Min((_until - _clock.GetUtcNow().UtcDateTime).Ticks,
                    (_duration - _clock.GetElapsedTime(_started)).Ticks));
            }
        }
    }

    public static GameSessionLease? TryCreate(SessionLeaseResponse response, string serverId, ushort worldId, TimeProvider clock)
    {
        if (!ValidDeadline(response, clock.GetUtcNow().UtcDateTime) || response.ServerId != serverId || response.WorldId != worldId ||
            !Positive(response.AccountId, out long account) || !Positive(response.FencingToken, out long fence) ||
            !long.TryParse(response.SessionEpoch, NumberStyles.None, CultureInfo.InvariantCulture, out long epoch) || epoch < 0 ||
            epoch.ToString(CultureInfo.InvariantCulture) != response.SessionEpoch || response.CredentialsVersion is null or < 0 ||
            !Guid.TryParseExact(response.GameSessionId, "D", out Guid session) || session == Guid.Empty ||
            !Guid.TryParseExact(response.GameContextId, "D", out Guid context) || context == Guid.Empty ||
            response.AccessLevel is not { } access || (access & (ushort)AccountAccessLevel.Player) == 0 || (access & ~(ushort)(AccountAccessLevel.Player | AccountAccessLevel.GameMaster | AccountAccessLevel.Admin | AccountAccessLevel.Console | AccountAccessLevel.Tournament | AccountAccessLevel.PTR)) != 0)
        {
            return null;
        }

        return new(response, new(new AccountId(account), session, fence), clock);
    }

    public bool TryRenew(SessionLeaseResponse response)
    {
        lock (_gate)
        {
            if (!ActiveUnderLock() || !ValidDeadline(response, _clock.GetUtcNow().UtcDateTime) ||
                response.AccountId != _identity.AccountId || response.GameSessionId != _identity.GameSessionId ||
                response.GameContextId != _identity.GameContextId || response.FencingToken != _identity.FencingToken ||
                response.ServerId != _identity.ServerId || response.WorldId != _identity.WorldId ||
                response.AccessLevel != _identity.AccessLevel || response.CredentialsVersion != _identity.CredentialsVersion ||
                response.SessionEpoch != _identity.SessionEpoch || response.LeaseUntil < _until)
            {
                return false;
            }

            SetDeadline(response.LeaseUntil!.Value);
            return true;
        }
    }

    public void Revoke() { lock (_gate) _revoked = true; }
    private bool ActiveUnderLock()
    {
        if (!_revoked && (_clock.GetUtcNow().UtcDateTime >= _until || _clock.GetElapsedTime(_started) >= _duration))
            _revoked = true;
        return !_revoked;
    }
    private void SetDeadline(DateTime until)
    {
        _until = until;
        _started = _clock.GetTimestamp();
        _duration = until - _clock.GetUtcNow().UtcDateTime;
    }
    private static bool Positive(string? text, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0 && value.ToString(CultureInfo.InvariantCulture) == text;
    private static bool ValidDeadline(SessionLeaseResponse response, DateTime now) =>
        response.State == GameAuthStates.Active && response.Error is null && response.LeaseUntil is { Kind: DateTimeKind.Utc } until &&
        response.AuthorizationUntil is { Kind: DateTimeKind.Utc } authorization && until > now && until <= now.Add(GameAuthPolicy.SessionLeaseLifetime) && until <= authorization;
}
