using System.Collections.Immutable;
using Avalon.Common.Accounts;
using Avalon.Common.Telemetry;
using Avalon.World.Configuration;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.GameAuth;

/// <summary>
/// The world's own ceiling on admitted accounts (#967): identity decides who enters, and this guards the pod if its
/// place ledger is wrong or was lost. The limit is the world's configuration (<c>Game:PlayerLimit</c>), set to the same
/// number as the world's stored limit; none configured, no backstop. Runs in the admission work, off the tick, after
/// identity activated the session. Should never fire: when it does, it logs at Error (at most once per
/// <see cref="FailureLogThrottle.Interval" />) and counts <c>world.admission.backstop_refusals</c>; a refusal means
/// identity's ledger was wrong or lost, or the two limits differ.
/// </summary>
public sealed partial class WorldBackstop
{
    private readonly int? _limit;
    private readonly TimeProvider _clock;
    private readonly ILogger<WorldBackstop> _logger;
    private readonly FailureLogThrottle _refusalLog = new();

    // The accounts one check counts, reused under its lock so a check allocates nothing once the set has grown.
    private readonly Lock _countGate = new();
    private readonly HashSet<long> _accounts = [];

    public WorldBackstop(GameConfiguration config, TimeProvider clock, ILogger<WorldBackstop> logger)
    {
        (_limit, _clock, _logger) = (config.PlayerLimit, clock, logger);
        Ceiling = config.PlayerLimit is { } limit ? CeilingOf(limit, config) : null;
        // Built with the packet handlers as the world starts: every start says which limit guards it.
        if (_limit is { } configured) Configured(logger, configured, Ceiling!.Value);
        else NotConfigured(logger);
    }

    /// <summary>The configured limit plus its margin; null with no limit configured.</summary>
    public int? Ceiling { get; }

    /// <summary>The limit plus the margin: <see cref="GameConfiguration.PlayerLimitMarginPercent" /> of it, at least <see cref="GameConfiguration.PlayerLimitMarginMinimum" />.</summary>
    public static int CeilingOf(int limit, GameConfiguration config) =>
        limit + Math.Max(config.PlayerLimitMarginMinimum, (int)Math.Ceiling(limit * config.PlayerLimitMarginPercent / 100.0));

    /// <summary>
    /// How long the backstop's session end may hold the admission lane and the source's slot (#967), as a failed
    /// admission's abandon does: past it the end is given up, and the session runs out with its lease (within 45 s), its
    /// place with it.
    /// </summary>
    public static readonly TimeSpan EndTimeout = TimeSpan.FromSeconds(2);

    /// <summary>A cancellation that bounds the backstop's session end by <see cref="EndTimeout" />, on this world's clock.</summary>
    public CancellationTokenSource EndDeadline() => new(EndTimeout, _clock);

    /// <summary>
    /// Whether <paramref name="admitting" />, whose session identity just activated, takes the world over its ceiling.
    /// It counts accounts, not connections: each account with a lease not revoked on a connection in
    /// <paramref name="connections" />, once, and this one's. So a takeover's old connection, whose lease stays until its
    /// next heartbeat is refused, never counts its account twice, and cycling takeovers cannot inflate the count. Staff
    /// (the Admin flag) are neither counted nor refused. A lease is read by <see cref="GameSessionLease.IsRevoked" />
    /// alone, with no lock and no clock: the tick marks a lease that ran out within one tick, which can only count an
    /// account a little longer. The set of accounts is reused under a lock, so a check allocates nothing once it has
    /// grown. A refusal is counted and logged here.
    /// </summary>
    public bool Refuses(GameSessionLease lease, ImmutableArray<IWorldConnection> connections, IWorldConnection admitting)
    {
        if (Ceiling is not { } ceiling || AccountAccessLevel.Admin.Allows(lease.AccessLevel)) return false;

        int admitted;
        lock (_countGate)
        {
            _accounts.Clear();
            _accounts.Add(lease.Authority.AccountId.Value);
            foreach (IWorldConnection connection in connections)
            {
                if (!ReferenceEquals(connection, admitting) &&
                    connection is WorldConnection { GameSessionLease: { IsRevoked: false } other } &&
                    !AccountAccessLevel.Admin.Allows(other.AccessLevel))
                {
                    _accounts.Add(other.Authority.AccountId.Value);
                }
            }

            admitted = _accounts.Count;
        }

        if (admitted <= ceiling) return false;

        DiagnosticsConfig.World.BackstopRefusals.Add(1);
        if (_refusalLog.TryTake(_clock, out int suppressed))
            Refused(_logger, lease.Authority.AccountId.Value, admitted, _limit!.Value, ceiling, suppressed);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Player limit {Limit}, backstop ceiling {Ceiling} (Game:PlayerLimit)")]
    private static partial void Configured(ILogger logger, int limit, int ceiling);

    [LoggerMessage(Level = LogLevel.Information, Message = "No player limit configured (Game:PlayerLimit): no backstop")]
    private static partial void NotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "The backstop refused account {AccountId}: {Admitted} admitted accounts, over the player limit {Limit} plus its margin ({Ceiling}). Identity's place ledger is wrong or was lost, or its stored limit is above this world's Game:PlayerLimit. {Suppressed} earlier refusals were not logged")]
    private static partial void Refused(ILogger logger, long accountId, int admitted, int limit, int ceiling, int suppressed);
}
