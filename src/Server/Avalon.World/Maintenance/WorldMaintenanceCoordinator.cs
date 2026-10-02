using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Social;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Threading;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maintenance;

/// <summary>
/// This world's maintenance state, its countdown and its cutoff. The applied state changes on the tick only
/// (#639): <see cref="ApplyCommitted" />, <see cref="Advance" /> and <see cref="RunIfEntryAllowed" /> run there,
/// and assert it. The off-tick readers (the Redis notification and the reconciliation) only offer the state they
/// read; the next <see cref="Advance" /> applies the newest offered revision. <see cref="InitializeAsync" /> runs
/// once, before the tick starts. <see cref="CurrentState" /> may be read from any thread.
/// </summary>
public sealed class WorldMaintenanceCoordinator(
    WorldId worldId,
    IWorldMaintenanceRepository repository,
    ICharacterSaver saver,
    TimeProvider clock,
    ILogger<WorldMaintenanceCoordinator> logger,
    TickThreadGuard? tickThread = null)
{
    private static readonly int[] Thresholds = [180, 60, 30, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0];

    // Written on the tick (or before it starts), read anywhere.
    private volatile WorldMaintenanceState? _state;

    // The newest state an off-tick reader saw; taken by the next Advance.
    private WorldMaintenanceState? _offered;

    // Tick only.
    private readonly HashSet<IWorldConnection> _closing = new(ReferenceEqualityComparer.Instance);
    private bool _startPending;
    private int _lastRemaining;

    // Shared between the tick, which starts closes, and the drain that waits for them.
    private readonly object _closesSync = new();
    private readonly List<Task> _closes = [];
    private Task? _drainTask;
    private Func<bool> _disconnectsProcessed = static () => true;

    /// <summary>The state this world has applied. Any thread.</summary>
    public WorldMaintenanceState? CurrentState => _state;

    /// <summary>
    /// The final spawn, on the tick: runs <paramref name="enter" /> only while the decision is still valid and,
    /// for a non-Admin, the cutoff this world has applied is not active.
    /// </summary>
    public bool RunIfEntryAllowed(IWorldConnection connection, WorldEntryDecision decision, Action enter)
    {
        tickThread?.AssertOnTick("WorldMaintenanceCoordinator.RunIfEntryAllowed");
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        if (!decision.IsValidAt(nowUtc) || _state is not { } state ||
            (state.IsCutoffActive(nowUtc) && (connection.AccessLevel & AccountAccessLevel.Admin) == 0))
            return false;

        enter();
        return true;
    }

    public void SetDrainObserver(Func<bool> disconnectsProcessed)
        => _disconnectsProcessed = disconnectsProcessed;

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
        _startPending = false;
        _lastRemaining = state.Enabled ? Math.Max(1, Remaining(state, clock.GetUtcNow().UtcDateTime)) : 0;
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
    /// deadline blocks and closes every authenticated non-Admin connection.
    /// </summary>
    public void Advance(DateTime nowUtc, IReadOnlyList<IWorldConnection> connections)
    {
        tickThread?.AssertOnTick("WorldMaintenanceCoordinator.Advance");
        if (Interlocked.Exchange(ref _offered, null) is { } offered)
            Apply(offered);

        if (_state is not { Enabled: true } state) return;

        int remaining = Remaining(state, nowUtc);
        string? message = null;
        if (_startPending && remaining > 0)
        {
            message = $"World maintenance has started. Non-admin players will be disconnected in {Duration(remaining)}.";
            _startPending = false;
        }
        else
        {
            int? crossed = Thresholds.LastOrDefault(threshold => _lastRemaining > threshold && remaining <= threshold);
            if (crossed.HasValue && (_lastRemaining > crossed.Value && remaining <= crossed.Value))
                message = $"World maintenance: {Duration(crossed.Value)} remaining. Non-admin players will be disconnected.";
        }

        _lastRemaining = remaining;
        if (message is not null)
            foreach (IWorldConnection connection in connections)
                if (connection.IsConnected && !connection.IsClosing && connection.InGame)
                    try
                    {
                        connection.Send(SChatMessagePacket.System(message, nowUtc, connection.CryptoSession.Encrypt));
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "World {WorldId} maintenance warning could not be sent to a client",
                            worldId.Value);
                    }

        if (remaining != 0) return;

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
            lock (_closesSync)
                if (_drainTask is null || _drainTask.IsCompleted)
                    _drainTask = Task.Run(DrainAndLogAsync, CancellationToken.None);
    }

    /// <summary>Waits for every maintenance close so far, the despawns they queue and the saves those start.</summary>
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
        _startPending = state.Enabled;
        _lastRemaining = state.Enabled ? int.MaxValue : 0;
        _closing.Clear();
        logger.LogInformation("World {WorldId} maintenance revision {Revision}: {Enabled}, deadline {Deadline}",
            worldId.Value, state.Revision, state.Enabled, state.DeadlineUtc);
    }

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
}
