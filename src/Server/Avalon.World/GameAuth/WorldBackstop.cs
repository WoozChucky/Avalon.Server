using System.Collections.Immutable;
using Avalon.Common.Accounts;
using Avalon.Common.Telemetry;
using Avalon.World.Configuration;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.GameAuth;

/// <summary>
/// The world's own ceiling on admitted connections (#967): identity decides who enters, and this guards the pod if its
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
    /// Whether <paramref name="admitting" />, whose session identity just activated, takes the world over its ceiling:
    /// the connections in <paramref name="connections" /> holding an active lease, and this one, counted with a plain loop
    /// that allocates nothing. Staff (the Admin flag) always pass. A refusal is counted and logged here.
    /// </summary>
    public bool Refuses(GameSessionLease lease, ImmutableArray<IWorldConnection> connections, IWorldConnection admitting)
    {
        if (Ceiling is not { } ceiling || AccountAccessLevel.Admin.Allows(lease.AccessLevel)) return false;

        int admitted = 1;
        foreach (IWorldConnection connection in connections)
        {
            if (!ReferenceEquals(connection, admitting) && connection is WorldConnection world &&
                world.GameSessionLease?.IsActive == true)
            {
                admitted++;
            }
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
        Message = "The backstop refused account {AccountId}: {Admitted} admitted connections, over the player limit {Limit} plus its margin ({Ceiling}). Identity's place ledger is wrong or was lost, or its stored limit is above this world's Game:PlayerLimit. {Suppressed} earlier refusals were not logged")]
    private static partial void Refused(ILogger logger, long accountId, int admitted, int limit, int ceiling, int suppressed);
}
