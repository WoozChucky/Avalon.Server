using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Social;
using Avalon.World.Configuration;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Maintenance;

/// <summary>
/// This world's maintenance state, its countdown and its cutoff. The applied state changes on the tick only
/// (#639): <see cref="ApplyCommitted" />, <see cref="Advance" /> and <see cref="RunIfEntryAllowed" /> run there,
/// and assert it. The off-tick readers (the Redis notification and the reconciliation) only offer the state they
/// read; the next <see cref="Advance" /> applies the newest offered revision. <see cref="InitializeAsync" /> runs
/// once, before the tick starts. <see cref="CurrentState" /> may be read from any thread.
/// <para>
/// A stop runs its restart drain here too (#768): <see cref="DrainForRestartAsync" /> offers it from the stopping
/// thread, and the tick runs its countdown on the same schedule. It is held in memory only, beside the persisted state
/// rather than in it: never written, never published, never replaced by a revision, and gone with the process.
/// </para>
/// </summary>
public sealed class WorldMaintenanceCoordinator(
    WorldId worldId,
    IWorldMaintenanceRepository repository,
    ICharacterSaver saver,
    TimeProvider clock,
    ILogger<WorldMaintenanceCoordinator> logger,
    IOptions<WorldShutdownConfiguration> shutdown,
    TickThreadGuard? tickThread = null)
{
    private static readonly int[] s_thresholds = [180, 60, 30, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0];

    /// <summary>How long past its deadline the stop waits for the tick to end a restart drain, should the tick not.</summary>
    public static readonly TimeSpan RestartTickBackstop = TimeSpan.FromSeconds(1);

    // Written on the tick (or before it starts), read anywhere.
    private volatile WorldMaintenanceState? _state;

    // The newest state an off-tick reader saw; taken by the next Advance.
    private WorldMaintenanceState? _offered;

    // The restart drain a stop asked for, once; taken by the next Advance.
    private RestartDrain? _restartOffered;
    private int _restartRequested;

    // Tick only.
    private readonly HashSet<IWorldConnection> _closing = new(ReferenceEqualityComparer.Instance);
    private readonly Countdown _countdown = new();
    private readonly Countdown _restartCountdown = new();
    private RestartDrain? _restart;
    private bool _restartCutoff;

    // Shared between the tick, which starts closes, and the drain that waits for them.
    private readonly Lock _closesSync = new();
    private readonly List<Task> _closes = [];
    private Task? _drainTask;
    private Func<bool> _disconnectsProcessed = static () => true;

    /// <summary>The state this world has applied. Any thread.</summary>
    public WorldMaintenanceState? CurrentState => _state;

    /// <summary>
    /// The final spawn, on the tick: runs <paramref name="enter" /> only while the decision is still valid and,
    /// for a non-Admin, neither the cutoff this world has applied nor its restart drain's is active.
    /// </summary>
    public bool RunIfEntryAllowed(IWorldConnection connection, WorldEntryDecision decision, Action enter)
    {
        tickThread?.AssertOnTick("WorldMaintenanceCoordinator.RunIfEntryAllowed");
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        if (!decision.IsValidAt(nowUtc) || _state is not { } state ||
            ((state.IsCutoffActive(nowUtc) || _restartCutoff) && (connection.AccessLevel & AccountAccessLevel.Admin) == 0))
        {
            return false;
        }

        enter();
        return true;
    }

    public void SetDrainObserver(Func<bool> disconnectsProcessed)
        => _disconnectsProcessed = disconnectsProcessed;

    /// <summary>
    /// Off the tick, from the stop (#768): offers a restart drain whose deadline is <c>World:Shutdown:DrainTime</c> from
    /// now, and returns once the tick ends it: at the deadline, or sooner once no non-Admin player is left. Returns at
    /// once when the drain time is zero, and on every call after the first. The tick must be running; should it not end
    /// the drain, this gives up just past the deadline, or when <paramref name="ct" /> (the host's stop timeout) is
    /// cancelled, so the close and the saves after it still run.
    /// </summary>
    public async Task DrainForRestartAsync(CancellationToken ct)
    {
        TimeSpan drainTime = shutdown.Value.DrainTime;
        if (drainTime <= TimeSpan.Zero || Interlocked.Exchange(ref _restartRequested, 1) != 0) return;

        var restart = new RestartDrain(clock.GetUtcNow().UtcDateTime + drainTime);
        Volatile.Write(ref _restartOffered, restart);
        logger.LogInformation("World {WorldId} is stopping: draining players until {Deadline}", worldId.Value,
            restart.DeadlineUtc);
        try
        {
            await restart.Ended.Task.WaitAsync(drainTime + RestartTickBackstop, clock, ct);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("World {WorldId} restart drain was not ended by the tick; stopping anyway", worldId.Value);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogWarning("World {WorldId} restart drain was cut short by the host's stop timeout", worldId.Value);
        }
    }

    /// <summary>
    /// Loads the persisted state before the listener opens and before the tick starts. A restart does not replay
    /// an old countdown: no start announcement, and only thresholds still ahead are announced.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        WorldMaintenanceState state = await repository.ReadAsync(worldId, ct)
            ?? throw new InvalidOperationException($"World {worldId.Value} has no maintenance row");
        if (_state is not null && _state.Revision >= state.Revision) return;
        _state = state;
        _countdown.Resume(state.Enabled ? Math.Max(1, Remaining(state, clock.GetUtcNow().UtcDateTime)) : 0);
    }

    /// <summary>Off the tick (the Redis notification): reads the row when the revision is newer, and offers it.</summary>
    public async Task ApplyNotificationAsync(long revision, CancellationToken ct)
    {
        if (revision <= Math.Max(_state?.Revision ?? -1, Volatile.Read(ref _offered)?.Revision ?? -1)) return;
        await ReconcileAsync(ct);
    }

    /// <summary>Off the tick: reads the authoritative row and offers it to the tick.</summary>
    public async Task ReconcileAsync(CancellationToken ct)
    {
        WorldMaintenanceState state = await repository.ReadAsync(worldId, ct)
            ?? throw new InvalidOperationException($"World {worldId.Value} has no maintenance row");
        Offer(state);
    }

    /// <summary>Any thread: keeps <paramref name="state" /> for the next tick unless a newer one is waiting.</summary>
    public void Offer(WorldMaintenanceState state)
    {
        while (true)
        {
            WorldMaintenanceState? current = Volatile.Read(ref _offered);
            if (current is not null && current.Revision >= state.Revision) return;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _offered, state, current), current)) return;
        }
    }

    /// <summary>On the tick: applies a committed transition (the world's own command) at once.</summary>
    public void ApplyCommitted(WorldMaintenanceState state)
    {
        tickThread?.AssertOnTick("WorldMaintenanceCoordinator.ApplyCommitted");
        Apply(state);
    }

    /// <summary>
    /// On the tick, before the session pass: applies the newest offered state, sends the warning due, and at the
    /// deadline blocks and closes every authenticated non-Admin connection. Then runs the restart drain, when a stop
    /// asked for one: the persisted countdown and cutoff go on beside it, and neither replaces the other.
    /// </summary>
    public void Advance(DateTime nowUtc, IReadOnlyList<IWorldConnection> connections)
    {
        tickThread?.AssertOnTick("WorldMaintenanceCoordinator.Advance");
        if (Interlocked.Exchange(ref _offered, null) is { } offered)
            Apply(offered);

        if (_state is { Enabled: true } state)
        {
            int remaining = Remaining(state, nowUtc);
            if (_countdown.Due(remaining) is { } line)
            {
                Broadcast(line.Start
                    ? $"World maintenance has started. Non-admin players will be disconnected in {Duration(line.Seconds)}."
                    : $"World maintenance: {Duration(line.Seconds)} remaining. Non-admin players will be disconnected.",
                    nowUtc, connections);
            }

            if (remaining == 0)
                CloseNonAdmins(connections);
        }

        AdvanceRestart(nowUtc, connections);
    }

    /// <summary>
    /// On the tick: the restart drain's line due, on the maintenance schedule. At its deadline it blocks every
    /// authenticated non-Admin connection and ends, and the stop then closes everyone as it always has. It ends sooner
    /// once no authenticated non-Admin connection is left.
    /// </summary>
    private void AdvanceRestart(DateTime nowUtc, IReadOnlyList<IWorldConnection> connections)
    {
        if (Interlocked.Exchange(ref _restartOffered, null) is { } offered)
        {
            _restart = offered;
            _restartCountdown.Start();
        }

        if (_restart is not { } restart || restart.Ended.Task.IsCompleted) return;

        int remaining = Math.Max(0, (int)Math.Ceiling((restart.DeadlineUtc - nowUtc).TotalSeconds));
        if (_restartCountdown.Due(remaining) is { } line)
        {
            Broadcast(line.Seconds == 0 ? "Restarting now." : $"The world restarts for an update in {Duration(line.Seconds)}.",
                nowUtc, connections);
        }

        if (remaining == 0)
        {
            // Closing is left to the stop, with the shutdown reason; this only stops their packets until then.
            _restartCutoff = true;
            foreach (IWorldConnection connection in connections)
            {
                if (IsNonAdminPlayer(connection))
                    (connection as IMaintenanceBlockable)?.BlockForMaintenance();
            }

            EndRestart(restart, "its deadline passed");
        }
        else if (!connections.Any(static connection =>
                     IsNonAdminPlayer(connection) && connection.IsConnected && !connection.IsClosing))
        {
            EndRestart(restart, "no non-admin player is left");
        }
    }

    private void EndRestart(RestartDrain restart, string reason)
    {
        logger.LogInformation("World {WorldId} restart drain ended: {Reason}", worldId.Value, reason);
        restart.Ended.TrySetResult();
    }

    private static bool IsNonAdminPlayer(IWorldConnection connection)
        => connection.AccountId is not null && !connection.AccessLevel.HasFlag(AccountAccessLevel.Admin);

    private void Broadcast(string message, DateTime nowUtc, IReadOnlyList<IWorldConnection> connections)
    {
        foreach (IWorldConnection connection in connections)
        {
            if (connection.IsConnected && !connection.IsClosing && connection.InGame)
            {
                try
                {
                    connection.Send(SChatMessagePacket.System(message, nowUtc, connection.CryptoSession.Encrypt));
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "World {WorldId} maintenance warning could not be sent to a client",
                        worldId.Value);
                }
            }
        }
    }

    /// <summary>Blocks every authenticated non-Admin connection and closes each once, after the zero line.</summary>
    private void CloseNonAdmins(IReadOnlyList<IWorldConnection> connections)
    {
        // A connection closed earlier in the cutoff is gone; forget it, so a long cutoff holds only the live ones.
        _closing.RemoveWhere(static connection => !connection.IsConnected);

        int newCloses = 0;
        foreach (IWorldConnection connection in connections)
        {
            if (connection.AccountId is null || (connection.AccessLevel & AccountAccessLevel.Admin) != 0)
                continue;

            (connection as IMaintenanceBlockable)?.BlockForMaintenance();
            if (!connection.IsConnected || connection.IsClosing || !_closing.Add(connection))
                continue;

            try
            {
                Task close = GracefulShutdownHelper.NotifyAndCloseAsync(connection,
                    "World is under maintenance", DisconnectReason.Maintenance, logger);
                lock (_closesSync) _closes.Add(close);
                newCloses++;
            }
            catch (Exception e)
            {
                _closing.Remove(connection);
                logger.LogWarning(e, "World {WorldId} maintenance close could not be started for a client",
                    worldId.Value);
            }
        }

        if (newCloses > 0)
        {
            lock (_closesSync)
            {
                if (_drainTask is null || _drainTask.IsCompleted)
                    _drainTask = Task.Run(DrainAndLogAsync, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Waits for every maintenance close so far, the despawns they queue and the saves those start. Nothing waits on it
    /// in production: its only effect there is the error <see cref="DrainAndLogAsync" /> logs when a drain does not
    /// finish. It changes no state.
    /// </summary>
    public async Task WhenDrainedAsync(CancellationToken ct)
    {
        int awaited = 0;
        while (true)
        {
            Task[] pending;
            lock (_closesSync) pending = _closes.Skip(awaited).ToArray();
            if (pending.Length > 0)
            {
                try
                {
                    await Task.WhenAll(pending).WaitAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "World {WorldId} maintenance close failed; waiting for saves",
                        worldId.Value);
                }
            }
            awaited += pending.Length;

            // The close event queues a despawn for the next tick; its save starts as that queue is
            // drained. Wait for that handoff before observing the saver's idle task.
            while (!_disconnectsProcessed())
                await Task.Delay(10, ct);
            await saver.WhenAllIdle().WaitAsync(ct);
            lock (_closesSync)
                if (_closes.Count == awaited) return;
        }
    }

    private void Apply(WorldMaintenanceState state)
    {
        if (_state is not null && state.Revision <= _state.Revision) return;
        _state = state;
        if (state.Enabled)
            _countdown.Start();
        else
            _countdown.Resume(0);
        _closing.Clear();
        logger.LogInformation("World {WorldId} maintenance revision {Revision}: {Enabled}, deadline {Deadline}",
            worldId.Value, state.Revision, state.Enabled, state.DeadlineUtc);
    }

    /// <summary>Started after the cutoff's closes; its only effect is the error log when the drain does not finish.</summary>
    private async Task DrainAndLogAsync()
    {
        try
        {
            await WhenDrainedAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "World {WorldId} maintenance disconnect drain failed", worldId.Value);
        }
    }

    private static int Remaining(WorldMaintenanceState state, DateTime nowUtc)
        => Math.Max(0, (int)Math.Ceiling(((state.DeadlineUtc ?? nowUtc) - nowUtc).TotalSeconds));

    private static string Duration(int seconds) => seconds switch
    {
        >= 60 when seconds % 60 == 0 => $"{seconds / 60} minute{(seconds == 60 ? "" : "s")}",
        _ => $"{seconds} second{(seconds == 1 ? "" : "s")}",
    };

    /// <summary>
    /// Which line of a countdown is due, on the tick: the start line once, then the lowest threshold crossed since the
    /// last tick, so a late tick skips stale ones.
    /// </summary>
    private sealed class Countdown
    {
        private bool _startPending;
        private int _lastRemaining;

        /// <summary>A new countdown: the start line, then every threshold.</summary>
        public void Start()
        {
            _startPending = true;
            _lastRemaining = int.MaxValue;
        }

        /// <summary>One already under way (a restarted world's): no start line, only thresholds below
        /// <paramref name="lastRemaining" />.</summary>
        public void Resume(int lastRemaining)
        {
            _startPending = false;
            _lastRemaining = lastRemaining;
        }

        public (bool Start, int Seconds)? Due(int remaining)
        {
            (bool Start, int Seconds)? line = null;
            if (_startPending && remaining > 0)
            {
                line = (true, remaining);
                _startPending = false;
            }
            else
            {
                int last = _lastRemaining;
                int? crossed = s_thresholds.LastOrDefault(threshold => last > threshold && remaining <= threshold);
                if (crossed.HasValue && (last > crossed.Value && remaining <= crossed.Value))
                    line = (false, crossed.Value);
            }

            _lastRemaining = remaining;
            return line;
        }
    }

    /// <summary>A stop's restart drain: its deadline, and the task the tick completes when the drain ends.</summary>
    private sealed class RestartDrain(DateTime deadlineUtc)
    {
        public DateTime DeadlineUtc { get; } = deadlineUtc;
        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
